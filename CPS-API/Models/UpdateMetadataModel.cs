namespace CPS_API.Models
{
    public enum UpdateMetadataAction
    {
        Update,
        CopyAndDelete,
        Move
    }

    public class UpdateMetadataModel
    {
        public UpdateMetadataAction Action { get; set; }

        public LocationMapping? NewLocation { get; set; }

        public UpdateMetadataModel(UpdateMetadataAction action, LocationMapping? newLocation = null)
        {
            Action = action;
            NewLocation = newLocation;
        }
    }
}