using CPS_API.Models;
using CPS_API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using System.Net;

namespace CPS_API.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class FilesController : ControllerBase
    {
        // GET
        [HttpGet]
        [Route("content/{contentId}")]
        //[Route("{contentId}/content")]
        public async Task<IActionResult> GetFileURL(string contentId)
        {
            throw new NotImplementedException();
        }

        [HttpGet]
        [Route("metadata/{contentId}")]
        //[Route("{contentId}/metadata")]
        public async Task<IActionResult> GetFileMetadata(string contentId)
        {
            throw new NotImplementedException();
        }


        // PUT
        [HttpPut]
        public async Task<IActionResult> CreateFile([FromBody] CpsFile file)
        {
            throw new NotImplementedException();
        }

        [HttpPut]
        [DisableFormValueModelBinding]
        [DisableRequestSizeLimit]
        [RequestFormLimits(MultipartBodyLengthLimit = int.MaxValue)]
        [Route("new/{classification}")]
        public async Task<IActionResult> CreateLargeFile(Classification classification)
        {
            throw new NotImplementedException();
        }


        // POST
        [HttpPut]
        [Route("content/{contentId}")]
        //[Route("{contentId}/content")]
        public async Task<IActionResult> UpdateFileContent(string contentId, [FromBody] byte[] content)
        {
            throw new NotImplementedException();
        }

        [HttpPut]
        [Route("metadata/{contentId}")]
        //[Route("{contentId}/metadata")]
        public async Task<IActionResult> UpdateFileMetadata(string contentId, [FromBody] FileInformation fileInfo)
        {
            throw new NotImplementedException();
        }
    }
}
