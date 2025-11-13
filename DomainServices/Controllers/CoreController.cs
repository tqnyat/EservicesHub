using DomainServices.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using OfficeOpenXml;
using System.Data;
using System.Drawing;

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


        [HttpGet]
        [Route("LoadViewList")]
        public IActionResult LoadViewList()
        {
            try
            {
                if (User.Identity.IsAuthenticated)
                {
                    var sessionKey = HttpContext.Session.GetString("sessionKey");
                    var userName = User.Identity.Name;
                    if (!_coreService.IsValidSession(this.User, HttpContext.Session, sessionKey).Result)
                    {
                        return BadRequest($"Invalid Session|{userName}");
                    }
                    return Ok(JsonConvert.SerializeObject(_coreService.ILoadViewList(this.User, HttpContext.Session)));
                }
                else return Ok("");
            }
            catch (Exception err)
            {
                return StatusCode(500, new { ExceptionError = _commonServices.getExceptionErrorMessage(err) });
            }
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Route("LoadView")]
        public IActionResult LoadView([FromBody] Dictionary<string, string> data)
        {
            try
            {
                var sessionKey = HttpContext.Session.GetString("sessionKey");
                var userName = User.Identity.Name;
                if (!_coreService.IsValidSession(this.User, HttpContext.Session, sessionKey).Result)
                {
                    return BadRequest($"Invalid Session|{userName}");
                }
                return Ok(JsonConvert.SerializeObject(_coreService.ILoadView(data, this.User, HttpContext.Session)));
            }
            catch (Exception err)
            {
                return StatusCode(500, new { ExceptionError = _commonServices.getExceptionErrorMessage(err) });
            }
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Route("GetListDetial")]
        public IActionResult GetTemplate([FromBody] Dictionary<string, string> data)
        {
            try
            {
                var sessionKey = HttpContext.Session.GetString("sessionKey");
                var userName = User.Identity.Name;
                if (!_coreService.IsValidSession(this.User, HttpContext.Session, sessionKey).Result)
                {
                    return BadRequest($"Invalid Session|{userName}");
                }
                return Ok(JsonConvert.SerializeObject(_coreService.IGetTemplate(data, this.User)));
            }
            catch (Exception err)
            {
                return StatusCode(500, new { ExceptionError = _commonServices.getExceptionErrorMessage(err) });
            }
        }

        [Authorize]
        [HttpPost]
        [Route("SubmitData")]
        [ValidateAntiForgeryToken]
        public IActionResult SubmitData([FromBody] Dictionary<string, object> data)
        {
            try
            {
                var sessionKey = HttpContext.Session.GetString("sessionKey");
                var userName = User.Identity.Name;
                if (!_coreService.IsValidSession(this.User, HttpContext.Session, sessionKey).Result)
                {
                    return BadRequest($"Invalid Session|{userName}");
                }
                return Ok(JsonConvert.SerializeObject(_coreService.ISubmitData(data, this.User, HttpContext.Session)));
            }
            catch (Exception err)
            {
                return StatusCode(500, new { ExceptionError = _commonServices.getExceptionErrorMessage(err) });
            }
        }
        [ValidateAntiForgeryToken]
        [Authorize]
        [HttpPost]
        [Route("LoadData")]
        public IActionResult LoadData([FromBody] Dictionary<string, string> data)
        {
            try
            {
                var sessionKey = HttpContext.Session.GetString("sessionKey");
                var userName = User.Identity.Name;
                if (!_coreService.IsValidSession(this.User, HttpContext.Session, sessionKey).Result)
                {
                    return BadRequest($"Invalid Session|{userName}");
                }
                return Ok(JsonConvert.SerializeObject(_coreService.ILoadData(data, this.User, HttpContext.Session)));
            }
            catch (Exception err)
            {
                return StatusCode(500, new { ExceptionError = _commonServices.getExceptionErrorMessage(err) });
            }
        }

        [ValidateAntiForgeryToken]
        [Authorize]
        [HttpPost]
        [Route("LoadDetailData")]
        public IActionResult LoadDetailData([FromBody] Dictionary<string, string> data)
        {
            try
            {
                var sessionKey = HttpContext.Session.GetString("sessionKey");
                var userName = User.Identity.Name;
                if (!_coreService.IsValidSession(this.User, HttpContext.Session, sessionKey).Result)
                {
                    return BadRequest($"Invalid Session|{userName}");
                }
                return Ok(JsonConvert.SerializeObject(_coreService.ILoadDetailData(data, this.User, HttpContext.Session)));
            }
            catch (Exception err)
            {
                return StatusCode(500, new { ExceptionError = _commonServices.getExceptionErrorMessage(err) });
            }
        }

        [ValidateAntiForgeryToken]
        [Authorize]
        [HttpPost]
        [Route("EditRowData")]
        public IActionResult EditRowData([FromBody] Dictionary<string, string> data)
        {
            try
            {
                var sessionKey = HttpContext.Session.GetString("sessionKey");
                var userName = User.Identity.Name;
                if (!_coreService.IsValidSession(this.User, HttpContext.Session, sessionKey).Result)
                {
                    return BadRequest($"Invalid Session|{userName}");
                }
                return Ok(JsonConvert.SerializeObject(_coreService.IEditRowData(data, this.User, HttpContext.Session)));
            }
            catch (Exception err)
            {
                return StatusCode(500, new { ExceptionError = _commonServices.getExceptionErrorMessage(err) });
            }
        }

        [ValidateAntiForgeryToken]
        [Authorize]
        [HttpPost]
        [Route("DeleteRowData")]
        public IActionResult DeleteRowData([FromBody] Dictionary<string, string> data)
        {
            try
            {
                var sessionKey = HttpContext.Session.GetString("sessionKey");
                var userName = User.Identity.Name;
                if (!_coreService.IsValidSession(this.User, HttpContext.Session, sessionKey).Result)
                {
                    return BadRequest($"Invalid Session|{userName}");
                }
                return Ok(JsonConvert.SerializeObject(_coreService.IDeleteRowData(data, this.User, HttpContext.Session)));
            }
            catch (Exception err)
            {
                return StatusCode(500, new { ExceptionError = _commonServices.getExceptionErrorMessage(err) });
            }
        }


        //[Authorize]
        //[HttpPost]
        //[Route("PostField")]
        //public IActionResult PostField([FromBody] Dictionary<string, string> data)
        //{
        //    try
        //    {
        //        var sessionKey = HttpContext.Session.GetString("sessionKey");
        //        if (!_coreService.IsValidSession(this.User, HttpContext.Session, sessionKey).Result)
        //        {
        //            return BadRequest("Invalid Session");
        //        }
        //        return Ok(JsonConvert.SerializeObject(_coreService.IPostField(data, this.User, HttpContext.Session)));

        //    }
        //    catch (Exception err)
        //    {
        //        return StatusCode(500, new { ExceptionError = _commonServices.getExceptionErrorMessage(err) });
        //    }
        //}


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
