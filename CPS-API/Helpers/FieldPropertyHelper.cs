using System.Globalization;

namespace CPS_API.Helpers
{
    public static class FieldPropertyHelper
    {
        public static T GetFieldValue<T>(object parent, string fieldname)
        {
            var property = parent.GetType().GetProperty(fieldname);
            if (property != null)
                return (T)property.GetValue(parent);

            return default(T);
        }

        public static void SetFieldValue(object parent, string fieldname, object? value)
        {
            var property = parent.GetType().GetProperty(fieldname);
            if (property == null) throw new ArgumentException("Unknown property " + fieldname);

            if (property.PropertyType == typeof(int?))
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
            else if (property.PropertyType == typeof(decimal?))
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
            else if (property.PropertyType == typeof(DateTime?))
            {
                var stringValue = value?.ToString();
                if (stringValue == null)
                {
                    property.SetValue(parent, null, null);
                }
                else
                {
                    var dateParsed = DateTime.TryParse(stringValue, out var dateValue);
                    if (dateParsed)
                    {
                        property.SetValue(parent, dateValue, null);
                    }
                    else
                    {
                        property.SetValue(parent, null, null);
                    }
                }
            }
            else if (property.PropertyType == typeof(bool))
            {
                var stringValue = value?.ToString();
                if (stringValue == null)
                {
                    property.SetValue(parent, null, null);
                }
                else
                {
                    var boolParsed = Boolean.TryParse(stringValue, out var boolValue);
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
            else if (property.PropertyType == typeof(string))
            {
                var stringValue = value?.ToString();
                property.SetValue(parent, stringValue, null);
            }
            else
            {
                property.SetValue(parent, value, null);
            }
        }
    }
}
