﻿using System.Globalization;
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
            var property = parent.GetType().GetProperties().First(p => p.Name.Equals(fieldname, StringComparison.InvariantCultureIgnoreCase));
            if (property == null) throw new ArgumentException("Unknown property " + fieldname);

            if (property.PropertyType == typeof(int?))
            {
                SetIntegerValue(property, parent, value);
            }
            else if (property.PropertyType == typeof(decimal?))
            {
                SetDecimalValue(property, parent, value);
            }
            else if (property.PropertyType == typeof(DateTime?))
            {
                SetDateTimeValue(property, parent, value);
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
                var decimalValue = Convert.ToDecimal(stringValue, CultureInfo.CurrentCulture);
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
                var decimalValue = Convert.ToDecimal(stringValue, CultureInfo.CurrentCulture);
                if (decimalValue % 1 == 0)
                {
                    property.SetValue(parent, decimalValue, null);
                }
            }
        }

        public static void SetDateTimeValue(PropertyInfo property, object parent, object? value)
        {
            var stringValue = value?.ToString();
            if (stringValue == null)
            {
                property.SetValue(parent, null, null);
            }
            else
            {
                var dateParsed = DateTime.TryParse(stringValue, CultureInfo.CurrentCulture, out var dateValue);
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

        public static bool PropertyContainsData(object? value, object? defaultValue, PropertyInfo propertyInfo)
        {
            if (propertyInfo.PropertyType == typeof(int))
            {
                if (IntegerPropertyContainsData(value, defaultValue))
                {
                    return true;
                }
            }
            else if (propertyInfo.PropertyType == typeof(DateTime?))
            {
                if (DateTimePropertyContainsData(value, defaultValue))
                {
                    return true;
                }
            }
            else if (propertyInfo.PropertyType == typeof(string))
            {
                if (StringPropertyContainsData(value, defaultValue))
                {
                    return true;
                }
            }
            else if (value != defaultValue)
            {
                return true;
            }
            return false;
        }

        private static bool IntegerPropertyContainsData(object? value, object? defaultValue)
        {
            var stringValue = value?.ToString();
            var stringDefaultValue = defaultValue?.ToString();
            var decimalValue = Convert.ToDecimal(stringValue, new CultureInfo("en-US"));
            var decimalDefaultValue = Convert.ToDecimal(stringDefaultValue, new CultureInfo("en-US"));
            if (decimalValue != decimalDefaultValue)
            {
                return true;
            }
            return false;
        }

        private static bool DateTimePropertyContainsData(object? value, object? defaultValue)
        {
            var stringValue = value?.ToString();
            var dateParsed = DateTime.TryParse(stringValue, CultureInfo.CurrentCulture, out DateTime dateTimeValue);
            DateTime? nullableDateValue = null;
            if (dateParsed)
            {
                nullableDateValue = dateTimeValue;
            }
            var stringDefaultValue = defaultValue?.ToString();
            dateParsed = DateTime.TryParse(stringDefaultValue, CultureInfo.CurrentCulture, out DateTime dateTimeDefaultValue);
            DateTime? nullableDateDefaultValue = null;
            if (dateParsed)
            {
                nullableDateDefaultValue = dateTimeDefaultValue;
            }
            if (nullableDateValue != nullableDateDefaultValue)
            {
                return true;
            }
            return false;
        }

        private static bool StringPropertyContainsData(object? value, object? defaultValue)
        {
            var stringValue = value?.ToString();
            var stringDefaultValue = defaultValue?.ToString();
            if (stringValue != stringDefaultValue)
            {
                return true;
            }
            return false;
        }
    }
}