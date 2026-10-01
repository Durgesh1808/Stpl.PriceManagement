using System;
using System.Data;
using Microsoft.Data.SqlClient;

namespace Stpl.PriceManagement.Infrastructure.Data
{
    /// <summary>
    /// Short ways to add stored-procedure parameters. A null value is always
    /// sent as SQL NULL.
    /// </summary>
    public static class SqlParameterExtensions
    {
        /// <summary>Adds a parameter whose type SqlClient infers from the value.</summary>
        public static SqlParameterCollection Value(this SqlParameterCollection parameters, string name, object value)
        {
            parameters.Add(new SqlParameter(Name(name), value ?? DBNull.Value));
            return parameters;
        }

        /// <summary>A calendar date (SQL DATE): departure dates.</summary>
        public static SqlParameterCollection Date(this SqlParameterCollection parameters, string name, DateTime? value)
        {
            parameters.Add(new SqlParameter(Name(name), SqlDbType.Date)
            {
                Value = value.HasValue ? (object)value.Value.Date : DBNull.Value
            });
            return parameters;
        }

        /// <summary>
        /// A DATETIME2 - stated explicitly, because left to infer a DateTime
        /// travels as the legacy DATETIME (3 ms precision), which no longer
        /// equals the DATETIME2(0) column it is compared against.
        /// </summary>
        public static SqlParameterCollection DateTime2(this SqlParameterCollection parameters, string name, DateTime? value)
        {
            parameters.Add(new SqlParameter(Name(name), SqlDbType.DateTime2)
            {
                Value = value.HasValue ? (object)value.Value : DBNull.Value
            });
            return parameters;
        }

        /// <summary>A table-valued parameter of the given SQL table type.</summary>
        public static SqlParameterCollection Table(
            this SqlParameterCollection parameters, string name, string typeName, DataTable table)
        {
            parameters.Add(new SqlParameter(Name(name), SqlDbType.Structured)
            {
                TypeName = typeName,
                Value = table
            });
            return parameters;
        }

        private static string Name(string name)
        {
            return name.StartsWith("@", StringComparison.Ordinal) ? name : "@" + name;
        }
    }
}
