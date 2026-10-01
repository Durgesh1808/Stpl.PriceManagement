using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Stpl.PriceManagement.Infrastructure.Validation
{
    /// <summary>
    /// The sentences a request failed on, in the order the rules ran.
    /// </summary>
    /// <remarks>
    /// Plain C#, no validation library. The default sentences match the ones
    /// the old FluentValidation rules produced ("'Hub' must not be empty."),
    /// and messages for the same field are kept together, so a person sees the
    /// same text they saw before.
    /// </remarks>
    public sealed class ValidationErrors
    {
        private readonly List<KeyValuePair<string, List<string>>> _errors =
            new List<KeyValuePair<string, List<string>>>();

        public bool HasErrors
        {
            get { return _errors.Count > 0; }
        }

        /// <summary>Every message, joined into one line for a toast.</summary>
        public string Message
        {
            get { return string.Join(" ", _errors.SelectMany(e => e.Value)); }
        }

        public void Add(string field, string message)
        {
            foreach (var entry in _errors)
            {
                if (string.Equals(entry.Key, field, StringComparison.Ordinal))
                {
                    entry.Value.Add(message);
                    return;
                }
            }

            _errors.Add(new KeyValuePair<string, List<string>>(field, new List<string> { message }));
        }

        /// <summary>
        /// Not null, not blank, not an empty list and not a type's default
        /// value (a DateTime of 0001-01-01, a zero).
        /// </summary>
        public void NotEmpty(string field, object value, string message = null)
        {
            if (IsEmpty(value))
            {
                Add(field, message ?? "'" + Display(field) + "' must not be empty.");
            }
        }

        /// <summary>At most <paramref name="max"/> characters. Null passes.</summary>
        public void MaxLength(string field, string value, int max, string message = null)
        {
            if (value != null && value.Length > max)
            {
                Add(field, message ?? "The length of '" + Display(field) + "' must be " + max
                    + " characters or fewer. You entered " + value.Length + " characters.");
            }
        }

        private static bool IsEmpty(object value)
        {
            if (value == null)
            {
                return true;
            }

            var text = value as string;
            if (text != null)
            {
                return string.IsNullOrWhiteSpace(text);
            }

            var list = value as IEnumerable;
            if (list != null)
            {
                return !list.GetEnumerator().MoveNext();
            }

            var type = value.GetType();
            return type.IsValueType && value.Equals(Activator.CreateInstance(type));
        }

        /// <summary>"Occupancies[0].LandCostFx" -> "Land Cost Fx".</summary>
        private static string Display(string field)
        {
            var name = field;
            var dot = name.LastIndexOf('.');
            if (dot >= 0)
            {
                name = name.Substring(dot + 1);
            }

            var bracket = name.IndexOf('[');
            if (bracket >= 0)
            {
                name = name.Substring(0, bracket);
            }

            var builder = new StringBuilder();
            for (var i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i])
                    && (char.IsLower(name[i - 1])
                        || (i + 1 < name.Length && char.IsLower(name[i + 1]) && char.IsUpper(name[i - 1]))))
                {
                    builder.Append(' ');
                }

                builder.Append(name[i]);
            }

            return builder.ToString();
        }
    }
}
