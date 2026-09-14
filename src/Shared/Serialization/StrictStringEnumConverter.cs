using System.Text.Json.Serialization;

namespace O2C.Shared.Serialization;

/// <summary>
/// Enum serializzati per nome e mai accettati come intero: un valore fuori enum è sempre un errore di input.
/// </summary>
public sealed class StrictStringEnumConverter<TEnum>() : JsonStringEnumConverter<TEnum>(namingPolicy: null, allowIntegerValues: false)
    where TEnum : struct, Enum;
