using Microsoft.Graph;

namespace CPS_API.Models
{
    public class ExternalReferenceItem
    {
        public ListItem ListItem { get; set; }

        public ExternalReferences ExternalReference { get; set; }

        public ExternalReferenceItem(ListItem listItem, ExternalReferences externalReference)
        {
            ListItem = listItem;
            ExternalReference = externalReference;
        }
    }
}
