using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace Waterjam.Core.Services.Modular;

/// <summary>
/// Loads external mod assemblies (DLLs) from user directories using an unloadable AssemblyLoadContext.
/// </summary>
public sealed class ModAssemblyLoader : IDisposable
{
    private readonly string _assemblyDirectory;
    private readonly string _assemblyPath;
    private readonly ModLoadContext _context;
    private readonly AssemblyDependencyResolver _resolver;
    private readonly ResolveEventHandler _appDomainResolve;
    private readonly Func<AssemblyLoadContext, AssemblyName, Assembly> _alcResolve;
    private Assembly _loadedAssembly;
    private bool _disposed;

    public ModAssemblyLoader(string assemblyPath)
    {
        _assemblyPath = assemblyPath;
        _assemblyDirectory = Path.GetDirectoryName(assemblyPath) ?? string.Empty;
        // Use default context for Godot-managed types to avoid cross-context issues.
        _context = null;
        _resolver = new AssemblyDependencyResolver(assemblyPath);
        _alcResolve = OnResolve;
        AssemblyLoadContext.Default.Resolving += _alcResolve;
    }

    public Assembly Load()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ModAssemblyLoader));
        if (_loadedAssembly != null) return _loadedAssembly;
        // Load the main mod assembly into the default context
        _loadedAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(_assemblyPath);
        return _loadedAssembly;
    }

    public object CreateInstance(string typeName)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ModAssemblyLoader));
        var asm = Load();
        var type = asm.GetType(typeName, throwOnError: true, ignoreCase: false);
        return Activator.CreateInstance(type);
    }

    public void Unload()
    {
        if (_disposed) return;
        _loadedAssembly = null;
        // Detach resolver; assemblies loaded into Default cannot be unloaded
        AssemblyLoadContext.Default.Resolving -= _alcResolve;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Unload();
        _disposed = true;
    }

    private Assembly OnResolve(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        var name = assemblyName.Name ?? string.Empty;
        // Share core and engine/game assemblies; let default resolve them
        if (name.StartsWith("System", StringComparison.Ordinal)
            || name.StartsWith("Microsoft", StringComparison.Ordinal)
            || name.StartsWith("Godot", StringComparison.Ordinal)
            || string.Equals(name, "GodotSharp", StringComparison.Ordinal)
            || string.Equals(name, "Waterjam", StringComparison.Ordinal)
            || string.Equals(name, "mscorlib", StringComparison.Ordinal)
            || string.Equals(name, "netstandard", StringComparison.Ordinal))
        {
            return null;
        }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            return context.LoadFromAssemblyPath(path);
        }

        return null;
    }

    private sealed class ModLoadContext : AssemblyLoadContext
    {
        private readonly string _baseDirectory;

        public ModLoadContext(string baseDirectory) : base(isCollectible: true)
        {
            _baseDirectory = baseDirectory;
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            // Ensure the main game assembly (this assembly) and engine assemblies are always shared
            var requestedName = assemblyName.Name ?? string.Empty;
            var executing = Assembly.GetExecutingAssembly();
            if (string.Equals(executing.GetName().Name, requestedName, StringComparison.OrdinalIgnoreCase))
            {
                return executing;
            }

            // Never load these from mod bin; resolve from default context only
            bool IsSharedAssembly(string name)
            {
                return name.StartsWith("System", StringComparison.Ordinal)
                       || name.StartsWith("Microsoft", StringComparison.Ordinal)
                       || name.StartsWith("Godot", StringComparison.Ordinal)
                       || string.Equals(name, "GodotSharp", StringComparison.Ordinal)
                       || string.Equals(name, "Waterjam", StringComparison.Ordinal)
                       || string.Equals(name, "mscorlib", StringComparison.Ordinal)
                       || string.Equals(name, "netstandard", StringComparison.Ordinal);
            }

            if (IsSharedAssembly(requestedName))
            {
                try
                {
                    return AssemblyLoadContext.Default.LoadFromAssemblyName(assemblyName);
                }
                catch
                {
                    // Fallthrough to scan already loaded default assemblies
                }
            }

            // Prefer assemblies already loaded in the default context to avoid duplicate loads
            var shared = AssemblyLoadContext.Default.Assemblies
                .FirstOrDefault(a => string.Equals(a.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
            if (shared != null)
            {
                return shared;
            }

            // Resolve dependencies from the same directory as the main mod assembly (safe third-party libs)
            var candidate = Path.Combine(_baseDirectory, requestedName + ".dll");
            if (File.Exists(candidate))
            {
                return LoadFromAssemblyPath(candidate);
            }

            return null;
        }
    }
}