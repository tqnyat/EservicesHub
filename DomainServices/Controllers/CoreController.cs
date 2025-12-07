using DomainServices.Models.Core;
using DomainServices.Services;
using EservicesHub.Models.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DomainServices.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class CoreController : ControllerBase
    {
        #region Filds
        private readonly CoreServices _coreService;
        #endregion

        #region Constructor
        public CoreController(CoreServices coreService)
        {
            _coreService = coreService;
        }
        #endregion

        #region Methods


        [Authorize, HttpGet("GetUserViews")]
        public async Task<IActionResult> GetUserViews()
        {
            var result = await _coreService.IGetUserViewsAsync(HttpContext.User);

            if (result == null)
                return NotFound();

            return Ok(result);
        }

        [Authorize, HttpPost("GetViewDefinition")]
        public async Task<IActionResult> GetViewDefinition([FromBody] Dictionary<string, string> data)
        {

            var result = await _coreService.ILoadView(data, HttpContext.User);

            return Ok(result);
        }

        [Authorize, HttpPost("GetListDetial")]
        public async Task<IActionResult> GetTemplate([FromBody] Dictionary<string, string> data)
        {

            var result = await _coreService.IGetTemplate(data, HttpContext.User);

            return Ok(result);
        }

        [Authorize, HttpPost("SubmitData")]
        public async Task<IActionResult> SubmitData([FromBody] SubmitDataRequest data)
        {
            var result = await _coreService.ISubmitData(data, HttpContext.User);

            return Ok(result);
        }

        [Authorize, HttpPost("LoadData")]
        public async Task<IActionResult> LoadData([FromBody] LoadDataRequest request)
        {
            var result = await _coreService.ILoadData(request, HttpContext.User);
            return Ok(result);
        }

        [Authorize, HttpPost("LoadDetailData")]
        public async Task<IActionResult> LoadDetailData([FromBody] LoadDetailDataRequest data)
        {

            var result = await _coreService.ILoadDetailData(data, HttpContext.User);

            return Ok(result);
        }

        [Authorize, HttpPost("EditRowData")]
        public async Task<IActionResult> EditRowData([FromBody] EditRowRequest data)
        {
            try
            {
                var result = await _coreService.IEditRowData(data, User);

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { ExceptionError = ex.Message });
            }
        }

        [Authorize, HttpPost("DeleteRowData")]
        public async Task<IActionResult> DeleteRowData([FromBody] DeleteRowRequest data)
        {
            var result = await _coreService.IDeleteRowData(data, HttpContext.User);

            return Ok(result);
        }

        [Authorize, HttpPost, Route("GetFile")]
        public async Task<IActionResult> GetFile([FromBody] IGetFileRequest data)
        {
            var result = await _coreService.IGetFile(data);

            return Ok(result);
        }

        [Authorize, HttpPost("ExportExcel")]
        public async Task<IActionResult> ExportExcel([FromBody] ExportExcelRequest data)
        {
            // Simply call the service, no session, no Excel, no extras
            DataShape ds = await _coreService.IExportExcel(data, HttpContext.User);

            return Ok(ds);
        }

        [Authorize, HttpPost("SetApplicationTheme")]
        public async Task<IActionResult> SetApplicationTheme([FromBody] ApplicationThemeRequest data)
        {
            var result = await _coreService.ISetApplicationTheme(data, HttpContext.User);

            return Ok(result);
            
        }

        [Authorize, HttpPost("GetSystemLookUp")]
        public async Task<IActionResult> GetSystemLookUp([FromBody] Dictionary<string, string> data)
        {
            var result = await _coreService.IGetSystemLookUp(data);

            return Ok(result);
        }

        [HttpPost("GetTopicsPublished")]
        public async Task<IActionResult> GetTopicsPublished()
        {
            var result = await _coreService.IGetTopicPublished();

            return Ok(result);
        }

        [ValidateAntiForgeryToken]
        [HttpPost]
        [Route("GetTopicDetial")]
        public async Task<IActionResult> GetTopicDetial(TopicDetailRequest data)
        {
            var result = await _coreService.IGetTopicDetial(data);

            return Ok(result);
        }

        [HttpPost]
        [Route("GenerateAndSendUser")]
        public async Task<IActionResult> GenerateAndSendUser([FromHeader] GenerateAndSendUserRequest data)
        {
            var result = await _coreService.IGenerateAndSendUser(data);

            return Ok(result);
            
        }
        #endregion
    }
}
