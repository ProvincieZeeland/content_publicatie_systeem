using System.Globalization;
using System.Reflection;

namespace CPS_API.Helpers
{
    public static class FieldPropertyHelper
    {
        public static T? GetFieldValue<T>(object parent, string fieldname)
        {
            var property = parent.GetType().GetProperty(fieldname);
            if (property != null)
            {
                var value = property.GetValue(parent);
                if (value != null)
                {
                    return (T)value;
                }
            }

            return default;
        }

        public static void SetFieldValue(object parent, string fieldname, object? value)
        {
            var property = parent.GetType().GetProperties().First(p => p.Name == fieldname);
            if (property == null) throw new ArgumentException("Unknown property " + fieldname);

            if (property.PropertyType == typeof(int?))
            {
                SetIntegerValue(property, parent, value);
            }
            else if (property.PropertyType == typeof(decimal?))
            {
                SetDecimalValue(property, parent, value);
            }
            else if (property.PropertyType == typeof(DateTimeOffset?))
            {
                SetDateTimeOffsetValue(property, parent, value);
            }
            else if (property.PropertyType == typeof(bool))
            {
                SetBooleanValue(property, parent, value);
            }
            else if (property.PropertyType == typeof(string))
            {
                SetStringValue(property, parent, value);
            }
            else
            {
                property.SetValue(parent, value, null);
            }
        }

        public static void SetIntegerValue(PropertyInfo property, object parent, object? value)
        {
            var stringValue = value?.ToString();
            if (stringValue == null)
            {
                property.SetValue(parent, null, null);
            }
            else
            {
                var decimalValue = Convert.ToDecimal(stringValue, new CultureInfo("en-US"));
                if (decimalValue % 1 == 0)
                {
                    property.SetValue(parent, (int)decimalValue, null);
                }
            }
        }

        public static void SetDecimalValue(PropertyInfo property, object parent, object? value)
        {
            var stringValue = value?.ToString();
            if (stringValue == null)
            {
                property.SetValue(parent, null, null);
            }
            else
            {
                var decimalValue = Convert.ToDecimal(stringValue, new CultureInfo("en-US"));
                if (decimalValue % 1 == 0)
                {
                    property.SetValue(parent, decimalValue, null);
                }
            }
        }

        public static void SetDateTimeOffsetValue(PropertyInfo property, object parent, object? value)
        {
            var stringValue = value?.ToString();
            if (stringValue == null)
            {
                property.SetValue(parent, null, null);
            }
            else
            {
                var boolParsed = DateTimeOffset.TryParse(stringValue, out var boolValue);
                if (boolParsed)
                {
                    property.SetValue(parent, boolValue, null);
                }
                else
                {
                    property.SetValue(parent, null, null);
                }
            }
        }

        public static void SetBooleanValue(PropertyInfo property, object parent, object? value)
        {
            var stringValue = value?.ToString();
            if (stringValue == null)
            {
                property.SetValue(parent, null, null);
            }
            else
            {
                var boolParsed = bool.TryParse(stringValue, out var boolValue);
                if (boolParsed)
                {
                    property.SetValue(parent, boolValue, null);
                }
                else
                {
                    property.SetValue(parent, null, null);
                }
            }
        }

        public static void SetStringValue(PropertyInfo property, object parent, object? value)
        {
            var stringValue = value?.ToString();
            property.SetValue(parent, stringValue, null);
        }
    }
}