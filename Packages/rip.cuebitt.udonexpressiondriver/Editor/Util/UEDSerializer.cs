using System;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

public static class UEDSerializer
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        Formatting = Formatting.Indented,
        Converters = { new Texture2DConverter() }
    };

    // Serialize any serializable object
    public static string Serialize<T>(T obj)
    {
        return JsonConvert.SerializeObject(obj, Settings);
    }

    // Deserialize any object type
    public static T Deserialize<T>(string json)
    {
        return JsonConvert.DeserializeObject<T>(json, Settings);
    }
}

// Converter for Texture2D fields
public class Texture2DConverter : JsonConverter<Texture2D>
{
    public override void WriteJson(JsonWriter writer, Texture2D value, JsonSerializer serializer)
    {
        if (value == null)
        {
            writer.WriteNull();
            return;
        }

        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out var guid, out long fileId);
        serializer.Serialize(writer, new TextureRef { guid = guid, fileId = fileId });
    }

    public override Texture2D ReadJson(JsonReader reader, Type objectType, Texture2D existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
        var reference = serializer.Deserialize<TextureRef>(reader);
        if (reference == null || string.IsNullOrEmpty(reference.guid))
            return null;

        var path = AssetDatabase.GUIDToAssetPath(reference.guid);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    [Serializable]
    private class TextureRef
    {
        public string guid;
        public long fileId;
    }
}