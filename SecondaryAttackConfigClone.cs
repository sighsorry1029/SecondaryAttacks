using System.Reflection;

namespace SecondaryAttacks;

internal static class SecondaryAttackConfigClone
{
    internal static T Shallow<T>(T source) where T : class, new()
    {
        T clone = new();
        foreach (PropertyInfo property in typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            property.SetValue(clone, property.GetValue(source, null), null);
        }

        return clone;
    }
}
