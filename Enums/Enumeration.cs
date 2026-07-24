using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MM4LB.Enums;

public abstract class Enumeration
{
    #region Properties
    public int Key
    {
        get;
    }

    public string Value
    {
        get;
    } = string.Empty;

    public override string ToString() => Value;
    #endregion


    #region Constructors
    protected Enumeration()
    {
    }

    protected Enumeration(int key, string value)
    {
        Key = key;
        Value = value;
    }
    #endregion


    #region Methods
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, object> _allByType = new();

    public static IEnumerable<T> GetAll<T>() where T : Enumeration, new()
    {
        // Cacheado por tipo: los campos estáticos del catálogo no cambian en toda la vida del proceso. Antes se
        // recorría por reflexión (con un new T() por campo, cuyo valor se ignoraba) en CADA llamada, y GetAll se
        // invoca decenas de miles de veces por carga (por cada GameImage, por cada <PlatformFolder>).
        return (IReadOnlyList<T>)_allByType.GetOrAdd(typeof(T), static type =>
        {
            var list = new List<T>();
            foreach (var info in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (info.GetValue(null) is T value)   // campo estático: no necesita instancia (antes: new T())
                    list.Add(value);
            }
            return list;
        });
    }

    public override bool Equals(object? obj)
    {
        var otherValue = obj as Enumeration;

        if (otherValue == null)
        {
            return false;
        }

        var typeMatches = GetType().Equals(otherValue.GetType());
        var valueMatches = Key.Equals(otherValue.Key);

        return typeMatches && valueMatches;
    }

    public override int GetHashCode()
    {
        return Key.GetHashCode();
    }

    public static T? FromKey<T>(int key) where T : Enumeration, new()
    {
        var matchingItem = Parse<T, int>(key, "Key", item => item.Key == key);
        return matchingItem;
    }

    public static T? FromValue<T>(string value) where T : Enumeration, new()
    {
        var matchingItem = Parse<T, string>(value, "Value", item => item.Value == value);
        return matchingItem;
    }

    #endregion


    #region Methods (private)
    private static T? Parse<T, K>(K value, string description, Func<T, bool> predicate) where T : Enumeration, new()
    {
        var matchingItem = GetAll<T>().FirstOrDefault(predicate);
        return matchingItem;
    }
    #endregion
}