using System.Reflection;
using System.Xml;
using CPS_API.Helpers;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using Constants = CPS_API.Models.Constants;

namespace CPS_API.Services
{
    public interface IXmlExportSerivce
    {
        string GetMetadataAsXml(FileInformation metadata);
    }

    public class XmlExportSerivce : IXmlExportSerivce
    {
        public string GetMetadataAsXml(FileInformation metadata)
        {
            if (metadata.Ids == null) throw new CpsException("No ID's found for metadata while getting xml.");

            using var sw = new StringWriter();
            var settings = new XmlWriterSettings
            {
                Indent = true,
                CloseOutput = true
            };
            using (var writer = XmlWriter.Create(sw, settings))
            {
                writer.WriteStartElement("Document");
                writer.WriteAttributeString("id", metadata.Ids.ObjectId);

                foreach (var propertyInfo in metadata.GetType().GetProperties())
                {
                    if (MetadataHelper.SkipFieldForXml(propertyInfo)) continue;

                    if (propertyInfo.PropertyType == typeof(FileMetadata))
                    {
                        WriteFileMetadataToXml(propertyInfo, metadata, writer);
                    }
                    else
                    {
                        writer.WriteStartElement(propertyInfo.Name);
                        writer.WriteString(GetPropertyValue(propertyInfo, metadata));
                        writer.WriteEndElement();
                    }
                }

                writer.WriteEndElement();

                writer.Flush();
                writer.Close();
            }
            return sw.ToString();
        }

        private void WriteFileMetadataToXml(PropertyInfo propertyInfo, FileInformation metadata, XmlWriter writer)
        {
            var value = propertyInfo.GetValue(metadata);
            if (value == null) throw new CpsException("Error while getting metadata XML: value is null");
            foreach (var secondPropertyInfo in value.GetType().GetProperties())
            {
                if (secondPropertyInfo.Name.Equals(Constants.ItemPropertyInfoName, StringComparison.InvariantCultureIgnoreCase)
                    || secondPropertyInfo.Name.Equals(nameof(FileMetadata.Source), StringComparison.InvariantCultureIgnoreCase))
                {
                    continue;
                }
                writer.WriteStartElement(secondPropertyInfo.Name);
                writer.WriteString(GetPropertyValue(secondPropertyInfo, value));
                writer.WriteEndElement();
            }
        }

        /// <summary>
        /// Dates are always assumed to be UTC! 
        /// Because we get metadata in UTC from SharePoint.
        /// </summary>
        private string? GetPropertyValue(PropertyInfo? propertyInfo, object obj)
        {
            ArgumentNullException.ThrowIfNull(propertyInfo);
            var value = propertyInfo.GetValue(obj);

            // Add timezone part for DateTime
            if (value is DateTime date)
            {
                var specified = DateTime.SpecifyKind(date, DateTimeKind.Utc);
                return specified.ToString("yyyy-MM-ddTHH:mm:sszzz");
            }

            return value == null ? string.Empty : value.ToString();
        }
    }
}