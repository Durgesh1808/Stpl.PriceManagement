using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Stpl.PriceManagement.Infrastructure.Data
{
    /// <summary>
    /// Turns a data reader's rows into objects, matching columns to
    /// properties by name (case-insensitive).
    /// </summary>
    /// <remarks>
    /// The rules, kept deliberately small:
    ///  - a column with no matching property is ignored;
    ///  - a property with no matching column keeps its default;
    ///  - NULL leaves the property at its default (null for a nullable type);
    ///  - numbers are converted between widths (TINYINT into int, BIGINT into
    ///    long, and so on), so a row class does not have to mirror every SQL type.
    /// </remarks>
    public static class RowMapper
    {
        private static readonly ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>> Properties =
            new ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>>();

        public static async Task<List<T>> ReadAllAsync<T>(SqlDataReader reader, CancellationToken cancellationToken)
            where T : new()
        {
            var rows = new List<T>();
            var setters = SettersFor(typeof(T), reader);

            while (await reader.ReadAsync(cancellationToken))
            {
                var item = new T();

                for (var i = 0; i < setters.Length; i++)
                {
                    var property = setters[i];
                    if (property == null || reader.IsDBNull(i))
                    {
                        continue;
                    }

                    property.SetValue(item, Convert(reader.GetValue(i), property.PropertyType));
                }

                rows.Add(item);
            }

            return rows;
        }

        /// <summary>One value converted to <typeparamref name="T"/>; default when null.</summary>
        public static T ConvertValue<T>(object value)
        {
            if (value == null)
            {
                return default(T);
            }

            return (T)Convert(value, typeof(T));
        }

        private static PropertyInfo[] SettersFor(Type type, SqlDataReader reader)
        {
            var properties = Properties.GetOrAdd(type, t =>
            {
                var map = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (property.CanWrite && property.GetSetMethod() != null)
                    {
                        map[property.Name] = property;
                    }
                }

                return map;
            });

            var setters = new PropertyInfo[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                PropertyInfo property;
                setters[i] = properties.TryGetValue(reader.GetName(i), out property) ? property : null;
            }

            return setters;
        }

        private static object Convert(object value, Type target)
        {
            var type = Nullable.GetUnderlyingType(target) ?? target;

            if (type.IsInstanceOfType(value))
            {
                return value;
            }

            if (type.IsEnum)
            {
                return value is string text
                    ? Enum.Parse(type, text, true)
                    : Enum.ToObject(type, value);
            }

            if (type == typeof(Guid))
            {
                return value is Guid ? value : Guid.Parse(value.ToString());
            }

            return System.Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        }
    }
}
