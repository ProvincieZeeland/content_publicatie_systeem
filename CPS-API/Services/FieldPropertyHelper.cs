using CamlBuilder;
using System.Globalization;
using System.Reflection;

namespace CPS_API.Services
{
    public static class FieldPropertyHelper
    {
        public static void SetFieldValue(object parent, PropertyInfo property, object? value)
        {
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
