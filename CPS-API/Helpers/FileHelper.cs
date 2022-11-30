using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using System.Text;

namespace CPS_API.Helpers
{
    public static class FileHelper
    {
        private const long _sizeLimit = 10L * 1024L * 1024L * 1024L; //10gb

        public static Encoding GetEncoding(MultipartSection section)
        {
            var hasMediaTypeHeader = MediaTypeHeaderValue.TryParse(section.ContentType, out var mediaType);

            // UTF-7 is insecure and shouldn't be honored. UTF-8 succeeds in most cases.
            if (!hasMediaTypeHeader || Encoding.UTF7.Equals(mediaType.Encoding))
            {
                return Encoding.UTF8;
            }

            return mediaType.Encoding;
        }

        public static async Task<byte[]> ProcessStreamedFile(
            MultipartSection section, ContentDispositionHeaderValue contentDisposition,
            ModelStateDictionary modelState)
        {
            try
            {
                using (var memoryStream = new MemoryStream())
                {
                    await section.Body.CopyToAsync(memoryStream);

                    // Check if the file is empty or exceeds the size limit.
                    if (memoryStream.Length == 0)
                    {
                        modelState.AddModelError("File", "The file is empty.");
                    }
                    else if (memoryStream.Length > _sizeLimit)
                    {
                        var megabyteSizeLimit = _sizeLimit / 1048576;
                        modelState.AddModelError("File",
                        $"The file exceeds {megabyteSizeLimit:N1} MB.");
                    }
                    else if (!IsValidFile(contentDisposition.FileName.Value, memoryStream))
                    {
                        modelState.AddModelError("File",
                            "The file type isn't permitted or the file's " +
                            "signature doesn't match the file's extension.");
                    }
                    else
                    {
                        return memoryStream.ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                modelState.AddModelError("File", $"The upload failed. Error: {ex.HResult}");
            }

            return Array.Empty<byte>();
        }

        private static bool IsValidFile(string fileName, Stream data)
        {
            if (string.IsNullOrEmpty(fileName) || data == null || data.Length == 0)
            {
                return false;
            }

            return true;
        }

    }
}
