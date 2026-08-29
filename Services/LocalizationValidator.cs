using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Resources;
using Tracker.Helpers;

namespace Tracker.Services;

/// <summary>
/// Validador dev-time de la localización: comprueba que toda clave declarada en <see cref="LocKeys"/> exista en el
/// recurso NEUTRO (<c>Strings/Resources.resx</c>). Sirve de red de seguridad contra claves rotas al migrar textos.
///
/// Solo corre en DEBUG (<see cref="ConditionalAttribute"/>): en Release se elimina la llamada. Si falta alguna clave,
/// lo deja en el log de excepciones (no rompe el arranque).
/// </summary>
public static class LocalizationValidator
{
    [Conditional("DEBUG")]
    public static void Validate()
    {
        var resourceManager = new ResourceManager("Tracker.Strings.Resources", typeof(LocKeys).Assembly);
        CultureInfo neutral = CultureInfo.GetCultureInfo("en");
        var missing = new List<string>();

        foreach (FieldInfo field in typeof(LocKeys).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (!field.IsLiteral || field.FieldType != typeof(string))
                continue;

            string key = (string)field.GetRawConstantValue()!;
            if (resourceManager.GetString(key, neutral) is null)
                missing.Add(key);
        }

        if (missing.Count > 0)
            ExceptionService.LogToFile(null, $"Localization: {missing.Count} missing key(s) in Resources.resx -> {string.Join(", ", missing)}");
    }
}
