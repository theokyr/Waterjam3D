using System;
using System.Reflection;

namespace Waterjam.Core.Services.Modular;

public sealed class ModAssemblyLoader : IDisposable
{
	private readonly Assembly _assembly;

	public ModAssemblyLoader(string assemblyPath)
	{
		_assembly = Assembly.LoadFrom(assemblyPath);
	}

	public object CreateInstance(string typeName)
	{
		var type = _assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
		if (type == null) return null;
		return Activator.CreateInstance(type);
	}

	public void Dispose()
	{
		// No explicit unload in .NET without AssemblyLoadContext custom contexts; keep minimal.
	}
}
