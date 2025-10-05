using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Waterjam.Core.Systems.Console;
using Waterjam.Core.Services.Modular;

namespace Waterjam.Core.Services.Network;

/// <summary>
/// Registry for mod-defined components that need network replication.
/// Dynamically handles serialization based on manifest declarations.
/// </summary>
public class ModComponentRegistry
{
    private readonly Dictionary<string, ComponentSerializer> _serializers = new();
    private readonly Dictionary<string, ComponentDeclaration> _declarations = new();

    /// <summary>
    /// Register a component from a mod manifest
    /// </summary>
    public void RegisterComponent(string modId, string componentName, ComponentDeclaration declaration)
    {
        var fullName = $"{modId}.{componentName}";

        var serializer = new ComponentSerializer(declaration.Fields);
        _serializers[fullName] = serializer;
        _declarations[fullName] = declaration;

        ConsoleSystem.Log(
            $"[ComponentRegistry] Registered {fullName} with {declaration.Fields.Count} fields (mode: {declaration.Mode})",
            ConsoleChannel.Network
        );
    }

    /// <summary>
    /// Serialize component data for network transmission
    /// </summary>
    public byte[] SerializeComponent(string fullName, Dictionary<string, object> data)
    {
        if (_serializers.TryGetValue(fullName, out var serializer))
        {
            return serializer.Serialize(data);
        }

        ConsoleSystem.LogWarn(
            $"[ComponentRegistry] Unknown component: {fullName}",
            ConsoleChannel.Network
        );
        return Array.Empty<byte>();
    }

    /// <summary>
    /// Deserialize component data from network
    /// </summary>
    public Dictionary<string, object> DeserializeComponent(string fullName, byte[] data)
    {
        if (_serializers.TryGetValue(fullName, out var serializer))
        {
            return serializer.Deserialize(data);
        }

        ConsoleSystem.LogWarn(
            $"[ComponentRegistry] Unknown component: {fullName}",
            ConsoleChannel.Network
        );
        return new Dictionary<string, object>();
    }

    /// <summary>
    /// Check if a component is registered
    /// </summary>
    public bool IsRegistered(string fullName)
    {
        return _serializers.ContainsKey(fullName);
    }

    /// <summary>
    /// Get replication mode for a component
    /// </summary>
    public ReplicationMode GetReplicationMode(string fullName)
    {
        return _declarations.TryGetValue(fullName, out var decl)
            ? decl.Mode
            : ReplicationMode.Full;
    }

    /// <summary>
    /// Get all registered components
    /// </summary>
    public IEnumerable<string> GetRegisteredComponents()
    {
        return _serializers.Keys;
    }
}

/// <summary>
/// Serializes component data based on field type declarations
/// </summary>
public class ComponentSerializer
{
    private readonly Dictionary<string, string> _fields; // field name → type

    public ComponentSerializer(Dictionary<string, string> fields)
    {
        _fields = fields ?? new Dictionary<string, string>();
    }

    public byte[] Serialize(Dictionary<string, object> data)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // Write field count
        writer.Write(_fields.Count);

        foreach (var (fieldName, fieldType) in _fields)
        {
            // Write field name
            writer.Write(fieldName);

            // Get value (null if not present)
            var hasValue = data.TryGetValue(fieldName, out var value);
            writer.Write(hasValue);

            if (!hasValue)
                continue;

            // Serialize based on declared type
            try
            {
                SerializeValue(writer, value, fieldType);
            }
            catch (Exception ex)
            {
                ConsoleSystem.LogErr(
                    $"[ComponentSerializer] Failed to serialize field {fieldName}: {ex.Message}",
                    ConsoleChannel.Network
                );
            }
        }

        return ms.ToArray();
    }

    public Dictionary<string, object> Deserialize(byte[] data)
    {
        var result = new Dictionary<string, object>();

        try
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            var fieldCount = reader.ReadInt32();

            for (int i = 0; i < fieldCount; i++)
            {
                var fieldName = reader.ReadString();
                var hasValue = reader.ReadBoolean();

                if (!hasValue)
                    continue;

                if (_fields.TryGetValue(fieldName, out var fieldType))
                {
                    var value = DeserializeValue(reader, fieldType);
                    if (value != null)
                    {
                        result[fieldName] = value;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr(
                $"[ComponentSerializer] Deserialization failed: {ex.Message}",
                ConsoleChannel.Network
            );
        }

        return result;
    }

    private void SerializeValue(BinaryWriter writer, object value, string type)
    {
        switch (type.ToLower())
        {
            case "int":
            case "int32":
                writer.Write(Convert.ToInt32(value));
                break;

            case "long":
            case "int64":
                writer.Write(Convert.ToInt64(value));
                break;

            case "float":
            case "single":
                writer.Write(Convert.ToSingle(value));
                break;

            case "double":
                writer.Write(Convert.ToDouble(value));
                break;

            case "bool":
            case "boolean":
                writer.Write(Convert.ToBoolean(value));
                break;

            case "string":
                writer.Write(value?.ToString() ?? string.Empty);
                break;

            case "vector3":
                if (value is Vector3 vec3)
                {
                    writer.Write(vec3.X);
                    writer.Write(vec3.Y);
                    writer.Write(vec3.Z);
                }

                break;

            case "vector2":
                if (value is Vector2 vec2)
                {
                    writer.Write(vec2.X);
                    writer.Write(vec2.Y);
                }

                break;

            case "color":
                if (value is Color color)
                {
                    writer.Write(color.R);
                    writer.Write(color.G);
                    writer.Write(color.B);
                    writer.Write(color.A);
                }

                break;

            default:
                ConsoleSystem.LogWarn(
                    $"[ComponentSerializer] Unsupported type: {type}",
                    ConsoleChannel.Network
                );
                break;
        }
    }

    private object DeserializeValue(BinaryReader reader, string type)
    {
        switch (type.ToLower())
        {
            case "int":
            case "int32":
                return reader.ReadInt32();

            case "long":
            case "int64":
                return reader.ReadInt64();

            case "float":
            case "single":
                return reader.ReadSingle();

            case "double":
                return reader.ReadDouble();

            case "bool":
            case "boolean":
                return reader.ReadBoolean();

            case "string":
                return reader.ReadString();

            case "vector3":
                return new Vector3(
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle()
                );

            case "vector2":
                return new Vector2(
                    reader.ReadSingle(),
                    reader.ReadSingle()
                );

            case "color":
                return new Color(
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle()
                );

            default:
                ConsoleSystem.LogWarn(
                    $"[ComponentSerializer] Unsupported type: {type}",
                    ConsoleChannel.Network
                );
                return null;
        }
    }
}