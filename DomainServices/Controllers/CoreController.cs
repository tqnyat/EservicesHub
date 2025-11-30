using DomainServices.Models.Core;
using DomainServices.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using OfficeOpenXml;
using System.Data;
using System.Drawing;
using System.Security.Claims;

namespace DomainServices.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class CoreController : ControllerBase
    {
        #region Filds
        private readonly CoreServices _coreService;
        private readonly CommonServices _commonServices;
        #endregion

        #region Constructor
        public CoreController(CoreServices coreService, CommonServices commonServices)
        {
            _coreService = coreService;
            _commonServices = commonServices;
        }
        #endregion

        #region Methods

        [HttpGet]
        [Route("IsAuthenticated")]
        public IActionResult IsAuthenticated()
        {
            try
            {
                return Ok(JsonConvert.SerializeObject(_coreService.IsAuthenticated(this.User)));
            }
            catch (Exception err)
            {
                return StatusCode(500, new { ExceptionError = _commonServices.getExceptionErrorMessage(err) });
            }
        }


        [Authorize]
        [HttpGet("GetUserViews")]
        public async Task<IActionResult> GetUserViews()
        {
            var result = await _coreService.IGetUserViewsAsync(HttpContext.User);

            if (result == null)
                return NotFound();

            return Ok(result);
        }

        [Authorize]
        [HttpPost("GetViewDefinition")]
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

        [Authorize]
        [HttpPost("SubmitData")]
        public IActionResult SubmitData([FromBody] Dictionary<string, object> body)
        {
            var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(body["data"].ToString());
            var session = JsonConvert.DeserializeObject<Dictionary<string, object>>(body["session"].ToString());

            var result = _coreService.ISubmitData(data, HttpContext.User, HttpContext.Session);

            return Ok(result);
        }

        [HttpPost("LoadData")]
        public async Task<IActionResult> LoadData([FromBody] LoadDataRequest t)
        {
            var result = await _coreService.ILoadData(t, HttpContext.User);
            return Ok(result);
        }

        [Authorize]
        [HttpPost("LoadDetailData")]
        public IActionResult LoadDetailData([FromBody] Dictionary<string, object> body)
        {
            var data = JsonConvert.DeserializeObject<Dictionary<string, string>>(body["data"].ToString());
            var session = JsonConvert.DeserializeObject<Dictionary<string, object>>(body["session"].ToString());

            //var result = _coreService.ILoadDetailData(data, HttpContext.User, session);

            return Ok();
        }


        [Authorize, HttpPost("EditRowData")]
        public async Task<IActionResult> EditRowData([FromBody] EditRowRequest data)
        {
            try
            {
                var result = await _coreService.IEditRowData(data, User);

                return Ok();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { ExceptionError = ex.Message });
            }
        }




        [Authorize]
        [HttpPost("DeleteRowData")]
        public IActionResult DeleteRowData([FromBody] Dictionary<string, object> body)
        {
            var data = JsonConvert.DeserializeObject<Dictionary<string, string>>(body["data"].ToString());
            var session = JsonConvert.DeserializeObject<Dictionary<string, object>>(body["session"].ToString());

            var result = _coreService.IDeleteRowData(data, HttpContext.User, HttpContext.Session);

            return Ok(result);
        }

        [ValidateAntiForgeryToken]
        [Authorize]
        [HttpPost]
        [Route("GetFile")]
        public IActionResult GetFile([FromBody] Dictionary<string, string> data)
        {
            try
            {
                var sessionKey = HttpContext.Session.GetString("sessionKey");
                var userName = User.Identity.Name;
                if (!_coreService.IsValidSession(this.User, HttpContext.Session, sessionKey).Result)
                {
                    return BadRequest($"Invalid Session|{userName}");
                }
                return Ok(JsonConvert.SerializeObject(_coreService.IGetFile(data, this.User, HttpContext.Session)));
            }
            catch (Exception err)
            {
                return StatusCode(500, new { ExceptionError = _commonServices.getExceptionErrorMessage(err) });
            }
        }


        [Authorize]
        [HttpGet]
        [Route("ExportExcel")]
        public IActionResult ExportExcel(string componentName)
        {
            try
            {
                var sessionKey = HttpContext.Session.GetString("sessionKey");
                var userName = User.Identity.Name;
                if (!_coreService.IsValidSession(this.User, HttpContext.Session, sessionKey).Result)
                {
                    return BadRequest("Invalid Session | " + userName);
                }
                DataTable dt = _coreService.IExportExcel(componentName, HttpContext.Session);

                if (dt != null && dt.Rows.Count > 0)
                {
                    string fileName = componentName + ".xlsx";
                    string contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

                    var stream = _commonServices.ExportDataTable("Approvers", dt);

                    return File(stream, contentType, fileName);
                }
                else
                {
                    ExcelPackage.License.SetNonCommercialPersonal("Colllecta");
                    using (var package = new ExcelPackage())
                    {
                        var worksheet = package.Workbook.Worksheets.Add("Sheet1");
                        Color myColor = Color.FromArgb(102, 68, 38, 207);

                        // Add column headers and set their style
                        for (int i = 0; i < dt.Columns.Count; i++)
                        {
                            worksheet.Cells[1, i + 1].Value = dt.Columns[i].ColumnName;
                            worksheet.Cells[1, i + 1].Style.Font.Bold = true; // Make text bold
                            worksheet.Cells[1, i + 1].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid; // Set fill pattern
                            worksheet.Cells[1, i + 1].Style.Fill.BackgroundColor.SetColor(myColor); // Set background color
                            worksheet.Cells[1, i + 1].Style.Font.Color.SetColor(System.Drawing.Color.White); // Set text color to white
                        }

                        worksheet.Cells.AutoFitColumns();
                        var stream = new MemoryStream();
                        package.SaveAs(stream);
                        string fileName = componentName + ".xlsx";
                        string contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                        return File(stream.ToArray(), contentType, fileName);
                    }
                }
            }
            catch (Exception err)
            {
                return BadRequest(new
                {
                    ExceptionError = _commonServices.getExceptionErrorMessage(err)
                });
            }
        }

        [ValidateAntiForgeryToken]
        [Authorize]
        [HttpPost]
        [Route("SetApplicationTheme")]
        public IActionResult SetApplicationTheme([FromBody] Dictionary<string, string> data)
        {
            try
            {
                var sessionKey = HttpContext.Session.GetString("sessionKey");
                var userName = User.Identity.Name;
                if (!_coreService.IsValidSession(this.User, HttpContext.Session, sessionKey).Result)
                {
                    return BadRequest($"Invalid Session|{userName}");
                }
                return Ok(JsonConvert.SerializeObject(_coreService.ISetApplicationTheme(data, this.User)));
            }
            catch (Exception err)
            {
                return StatusCode(500, new { ExceptionError = _commonServices.getExceptionErrorMessage(err) });
            }
        }

        [Authorize]
        [HttpPost]
        [Route("GetSystemLookUp")]
        public IActionResult GetSystemLookUp([FromBody] Dictionary<string, string> data)
        {
            try
            {
                var sessionKey = HttpContext.Session.GetString("sessionKey");
                var userName = User.Identity.Name;
                if (!_coreService.IsValidSession(this.User, HttpContext.Session, sessionKey).Result)
                {
                    return BadRequest($"Invalid Session|{userName}");
                }
                if (User == null || !User.Identity.IsAuthenticated)
                {
                    return StatusCode(401, "User must login");
                }
                return Ok(JsonConvert.SerializeObject(_coreService.IGetSystemLookUp(data)));
            }
            catch (Exception err)
            {
                return StatusCode(500, new { ExceptionError = _commonServices.getExceptionErrorMessage(err) });
            }
        }


        [ValidateAntiForgeryToken]
        [HttpPost]
        [Route("GetTopicsPublished")]
        public IActionResult GetTopicsPublished()
        {
            try
            {
                var sessionKey = HttpContext.Session.GetString("sessionKey");
                var userName = User.Identity.Name;
                if (!_coreService.IsValidSession(this.User, HttpContext.Session, sessionKey).Result)
                {
                    return BadRequest($"Invalid Session|{userName}");
                }
                return Ok(JsonConvert.SerializeObject(_coreService.IGetTopicPublished()));
            }
            catch (Exception err)
            {
                return StatusCode(500, new { ExceptionError = _commonServices.getExceptionErrorMessage(err) });
            }
        }
        [ValidateAntiForgeryToken]
        [HttpPost]
        [Route("GetTopicDetial")]
        public IActionResult GetTopicDetial(Dictionary<string, string> topicName)
        {
            try
            {
                var sessionKey = HttpContext.Session.GetString("sessionKey");
                var userName = User.Identity.Name;
                if (!_coreService.IsValidSession(this.User, HttpContext.Session, sessionKey).Result)
                {
                    return BadRequest($"Invalid Session|{userName}");
                }
                return Ok(JsonConvert.SerializeObject(_coreService.IGetTopicDetial(topicName)));
            }
            catch (Exception err)
            {
                return StatusCode(500, new { ExceptionError = _commonServices.getExceptionErrorMessage(err) });
            }
        }

        [ValidateAntiForgeryToken]
        [HttpPost]
        [Route("GenerateAndSendUser")]
        public IActionResult GenerateAndSendUser([FromHeader] string userId)
        {
            try
            {
                var sessionKey = HttpContext.Session.GetString("sessionKey");
                var userName = User.Identity.Name;
                if (!_coreService.IsValidSession(this.User, HttpContext.Session, sessionKey).Result)
                {
                    return BadRequest($"Invalid Session|{userName}");
                }
                return Ok(JsonConvert.SerializeObject(_coreService.IGenerateAndSendUser(userId)));
            }
            catch (Exception err)
            {
                return StatusCode(500, new { ExceptionError = _commonServices.getExceptionErrorMessage(err) });
            }
        }
        #endregion
    }
}
