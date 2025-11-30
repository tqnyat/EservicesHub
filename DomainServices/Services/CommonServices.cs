using DomainServices.Data;
using DomainServices.Data.Repository;
using DomainServices.Models;
using DomainServices.Models.Core;
using DomainServices.Services.Interfaces;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Net;
using System.Runtime.Serialization;
using System.Security.Permissions;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DomainServices.Services
{

    [Serializable]
    [System.Xml.Serialization.XmlRoot("PropertyEntry", Namespace = "", IsNullable = false)]
    [SecurityPermission(SecurityAction.Demand, Flags = SecurityPermissionFlag.SerializationFormatter)]
    public class CommonServices
    {

        #region Fields
        private readonly IConfiguration _configuration;
        private readonly ILogger<CommonServices> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly UserManager<Users> _userManager;
        private readonly SignInManager<Users> _signInManager;
        private readonly ILocalResourceService _localResourceService;
        private readonly DomainDBContext.DomainRepo _domainRepo;

        #endregion

        #region Constructor
        public CommonServices(
            IConfiguration configuration,
            ILocalResourceService localResourceService,
            ILogger<CommonServices> logger,
            IHttpContextAccessor httpContextAccessor,
            DomainDBContext.DomainRepo domainRepo,
            IWebHostEnvironment webHostEnvironment,
            UserManager<Users> userManager,
            SignInManager<Users> signInManager
            )
        {
            _configuration = configuration;
            _localResourceService = localResourceService;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
            _domainRepo = domainRepo;
            _webHostEnvironment = webHostEnvironment;
            _userManager = userManager;
            _signInManager = signInManager;
        }
        #endregion

        #region Methods

        public DataTable getDataTableFromQuery(string query, SqlCommand cmd, string ConnectionString = null, CommandType commandType = CommandType.Text)
        {
            if (ConnectionString == "")
            {
                ConnectionString = getConnectionString();
            }
            SqlConnection con = new SqlConnection();
            con.ConnectionString = ConnectionString;
            SqlDataAdapter da = null;
            try
            {
                if (cmd == null)
                {
                    cmd = new SqlCommand();
                }

                cmd.Connection = con;
                cmd.CommandType = commandType;
                cmd.CommandText = query;

                da = new SqlDataAdapter(cmd);
                DataTable dt = new DataTable();
                da.Fill(dt);
                return dt;
            }
            finally
            {
                if (con != null && con.State != ConnectionState.Closed)
                {
                    con.Close();
                    con.Dispose();
                    da.Dispose();
                }

            }
        }
        
        public string ExecuteQuery_OneValue(string query, SqlCommand cmd, string connectionString)
        {
            SqlConnection con = new SqlConnection();
            SqlTransaction sqlTrans = null;

            if (cmd == null)
            {
                cmd = new SqlCommand();
            }
            if (connectionString == null)
            {
                connectionString = getConnectionString();
            }


            con.ConnectionString = connectionString;

            //cmd.BindByName = true;
            try
            {
                con.Open();
                sqlTrans = con.BeginTransaction(IsolationLevel.ReadUncommitted);

                cmd.Connection = con;
                cmd.Transaction = sqlTrans;
                cmd.CommandText = query;

                object c = cmd.ExecuteScalar();
                sqlTrans.Commit();
                if (c != null)
                {
                    return c.ToString();
                }
                return null;
            }
            catch (Exception err)
            {
                
                sqlTrans.Rollback();
                throw;
            }
            finally
            {
                if (con.State != ConnectionState.Closed)
                {
                    con.Close();
                    con.Dispose();
                }
            }
        }
        
        public string ExecuteQuery_OneValue(string query, SqlCommand cmd, string connectionString, CommandType commandType)
        {
            SqlConnection con = new SqlConnection();
            SqlTransaction sqlTrans = null;

            if (cmd == null)
            {
                cmd = new SqlCommand();
            }
            if (connectionString == null)
            {
                connectionString = getConnectionString();
            }

            con.ConnectionString = connectionString;

            //cmd.BindByName = true;
            try
            {
                con.Open();
                sqlTrans = con.BeginTransaction(IsolationLevel.ReadUncommitted);

                cmd.Connection = con;
                cmd.Transaction = sqlTrans;
                cmd.CommandText = query;
                cmd.CommandType = commandType;

                object c = cmd.ExecuteScalar();
                sqlTrans.Commit();
                if (c != null)
                {
                    return c.ToString();
                }
                return null;
            }
            catch (Exception err)
            {
                sqlTrans.Rollback();
                throw;
            }
            finally
            {
                if (con.State != ConnectionState.Closed)
                {
                    con.Close();
                    con.Dispose();
                }
            }
        }
        
        public string ExecuteQuery_OneValue_Trans(string query, ref SqlConnection con, ref SqlTransaction P_sqlTrans, SqlCommand cmd, CommandType commandType = CommandType.Text)
        {
            if (cmd == null)
            {
                cmd = new SqlCommand();
            }

            cmd.Connection = con;
            cmd.Transaction = P_sqlTrans;
            cmd.CommandType = commandType;
            cmd.CommandText = query;

            object c = cmd.ExecuteScalar();
            if (c != null)
            {
                return c.ToString();
            }
            return null;

        }

        public int excuteQuery_Trans(string query, ref SqlConnection con, ref SqlTransaction P_sqlTrans, SqlCommand cmd, CommandType commandType = CommandType.Text)
        {
            if (cmd == null)
            {
                cmd = new SqlCommand();
            }

            cmd.Connection = con;
            cmd.Transaction = P_sqlTrans;
            cmd.CommandType = commandType;
            cmd.CommandText = query;
            return cmd.ExecuteNonQuery();
        }

        public int excuteQueryWithoutTrans(string query, SqlCommand cmd, string connectionString = null, CommandType commandType = CommandType.Text)
        {

            SqlConnection con = null;
            try
            {
                if (cmd == null)
                {
                    cmd = new SqlCommand();
                }

                if (connectionString == null)
                {
                    connectionString = getConnectionString();
                }

                con = new SqlConnection(connectionString);
                con.Open();

                cmd.Connection = con;
                cmd.CommandType = commandType;
                cmd.CommandText = query;
                int x = cmd.ExecuteNonQuery();
                return x;
            }
            catch (Exception err)
            {

                throw;
            }
            finally
            {
                if (con != null && con.State != ConnectionState.Closed)
                    con.Close();
            }
        }
                
        public string getConnectionString()
        {
            string EncryptionKey = Environment.GetEnvironmentVariable("EncryptionKey");

            var connectionString = _configuration.GetConnectionString("ApplicationDBContextConnection"); 
            connectionString = Encyption.Decrypt(connectionString, EncryptionKey); 
            return connectionString;
        }

        public string GenerateRandomPassword()
        {
            Random generator = new Random(); 
            const string upperChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            string randomUpperChar = upperChars[generator.Next(upperChars.Length)].ToString(); 
            const string lowerChars = "abcdefghijklmnopqrstuvwxyz";
            string randomLowerChar = lowerChars[generator.Next(lowerChars.Length)].ToString(); 
            string randomNumber = generator.Next(0, 10).ToString("D1"); 
            const string specialChars = "!@#$%^&*";
            string randomSpecialChar = specialChars[generator.Next(specialChars.Length)].ToString(); 
            string randomGuidSegment = Guid.NewGuid().ToString().Substring(0, 5); 
            string password = randomUpperChar + randomLowerChar + randomSpecialChar + randomGuidSegment + randomNumber; 
            string shuffledPassword = new string(password.ToCharArray().OrderBy(c => generator.Next()).ToArray()); 
            return shuffledPassword;
        }

        public string getExceptionErrorMessage(Exception err, bool isLocalized = true)
        {
            if (err.Message.Contains("ا"))
                return _localResourceService.GetResource(err.Message);

            if (err.Message.ToLower().Contains("unique key") || err.Message.ToLower().Contains("cannot insert duplicate key") 
                || (err.InnerException != null && err.InnerException.ToString().ToLower().Contains("unique key")))
                return isLocalized ? _localResourceService.GetResource("Message.Error.UniqueField") : "Message.Error.UniqueField";// + err.Message;

            if (err.Message.Contains("REFERENCE constraint"))
            {
                return isLocalized ? _localResourceService.GetResource("Message.Error.ConnectedRows") : "Message.Error.ConnectedRows";
            }

            if (err.Message.ToLower().Contains("required"))
            {
                return isLocalized ? string.Format(_localResourceService.GetResource("System.ErrorMessage.RequiredField"), err.InnerException?.Message) : err.Message;
            }
            
            if (err.Message.ToLower().Contains("system.errormessage"))
            {
                return isLocalized ? _localResourceService.GetResource(err.Message) : err.Message;
            }

            if (err.Message.Contains("FOREIGN KEY"))
                return err.Message;

            if (err.Message.ToLower().Contains("warring"))
                return err.Message;

            // log to db 
            // to be implemented

            // log error to file
            _logger.LogError(1, err, "GeneralException");
            return isLocalized ? _localResourceService.GetResource("Message.Error.Generic") : "Message.Error.Generic";
        }

        public string GetValue(object fieldValue)
        {
            try
            {
                if (fieldValue.ToString() == "" || fieldValue == null || fieldValue.ToString() == "null")
                    return "";
                else return fieldValue.ToString();
            }
            catch {
                return "";
            }
        }

        // added by salftii 29/4/2019  //////
        public string GetQueryWhere(string parentField, string querySearch, string queryDataAccess, string componentSearch, Users user)
        {
            var queryText = "";

            if (parentField != "")
            {
                queryText += " Where " + parentField + " = @ParentFieldValue ";
                queryText += ((querySearch != "") ? " AND " + querySearch : "");
                queryText += ((queryDataAccess != "") ? " AND " + queryDataAccess : "");
                queryText += ((componentSearch != "") ? " AND " + componentSearch : "");
            }
            else if (querySearch != "")
            {
                queryText += " Where " + querySearch;
                queryText += ((queryDataAccess != "") ? " AND " + queryDataAccess : "");
                queryText += ((componentSearch != "") ? " AND " + componentSearch : "");
            }
            else if (queryDataAccess != "")
            {
                queryText += " Where " + queryDataAccess;
                queryText += ((componentSearch != "") ? " AND " + componentSearch : "");
            }
            else if (componentSearch != "")
            {
                queryText += " Where " + componentSearch;
            }

            if (queryText.ToLower().Contains("userid()"))
            {
                queryText = CommonServices.ReplaceCaseInsensitive(queryText, "userid()", $"'{user.Id}'");
            }
            if (queryText.ToLower().Contains("groupid()"))
            {
                queryText = CommonServices.ReplaceCaseInsensitive(queryText, "groupid()", $"'{user.UserGroupId}'");
            }
            if (queryText.ToLower().Contains("roleid()"))
            {
                queryText = CommonServices.ReplaceCaseInsensitive(queryText, "roleid()", $"'{user.RoleId}'");
            }
            if (queryText.ToLower().Contains("langid()"))
            {
                queryText = CommonServices.ReplaceCaseInsensitive(queryText, "langid()", $"'{user.Language}'");
            }

            return queryText;
        }
        
        public Stream ExportDataTable(string workSheetName, DataTable dataTable)
        {
            ExcelPackage.License.SetNonCommercialPersonal("Colllecta");
            using (ExcelPackage pkg = new ExcelPackage())
            {
                ExcelWorksheet ws = pkg.Workbook.Worksheets.Add(workSheetName);
                var isRightToLeft = CultureInfo.CurrentCulture.TextInfo.IsRightToLeft;
                ws.View.RightToLeft = (isRightToLeft) ? true : false;
                var alignment = isRightToLeft ? ExcelHorizontalAlignment.Right : ExcelHorizontalAlignment.Left;
                ExcelRange range = null;
                var columnDeleted = 0;
                var columnToBeDeleted = 0;
                // Set columns number format
                List<string> columnsToDelete = new List<string>();

                foreach (DataRow dr in dataTable.Rows)
                {
                    for (int i = 1; i < dataTable.Columns.Count + 1; i++)
                    {
                        if (dataTable.Columns[i - 1].ColumnName.Contains("check_"))
                        {
                            // Create a new column with string datatype
                            string newColumnName = "System.Field." + dataTable.Columns[i - 1].ColumnName.Replace("check_", "");
                            if (!dataTable.Columns.Contains(newColumnName))
                            {
                                dataTable.Columns.Add(new DataColumn(newColumnName, typeof(string)));
                            }

                            // Convert values and copy them to the new column
                            foreach (DataRow row in dataTable.Rows)
                            {
                                var oldValue = row[dataTable.Columns[i - 1].ColumnName].ToString();
                                if (oldValue == "0")
                                {
                                    row[newColumnName] = _localResourceService.GetResource("System.Field.No");
                                }
                                else if (oldValue == "1")
                                {
                                    row[newColumnName] = _localResourceService.GetResource("System.Field.Yes");
                                }
                                else
                                {
                                    row[newColumnName] = oldValue; // Preserve original value if not 0 or 1
                                }
                            }

                            // Mark old column for deletion
                            columnsToDelete.Add(dataTable.Columns[i - 1].ColumnName);
                        }
                    }
                }
                foreach (var columnName in columnsToDelete)
                {
                    if (dataTable.Columns.Contains(columnName))
                    {
                        dataTable.Columns.Remove(columnName);
                    }
                }
                for (int i = 1; i <= dataTable.Columns.Count; i++)
                {
                    if (dataTable.Columns[i - 1].ColumnName.Contains("button_") ||
                        dataTable.Columns[i - 1].ColumnName.Contains("RowId") ||
                            ContainsArabicLetters(dataTable.Columns[i - 1].ColumnName))
                    {
                        columnDeleted++;
                        continue;
                    }
                    range = ws.Cells[2, i, dataTable.Rows.Count + 1, i];

                        if (dataTable.Columns[i - 1].DataType == typeof(String))
                        range.Style.Numberformat.Format = "@";
                    else if (dataTable.Columns[i - 1].DataType == typeof(Decimal))
                    {
                        if(dataTable.Columns[i - 1].ColumnName.Contains("Percentage"))
                        {
                            range.Style.Numberformat.Format = "0%";
                        }
                        else
                        {
                            range.Style.Numberformat.Format = "#.00";
                        }
                        
                    }
                    else if (dataTable.Columns[i - 1].DataType == typeof(Int32) || dataTable.Columns[i - 1].DataType == typeof(Int64))
                        range.Style.Numberformat.Format = "0";
                    range.Style.HorizontalAlignment = alignment;
                }

                int rowCount = 1;
                
                foreach (DataRow dr in dataTable.Rows)
                {
                    rowCount += 1;
                    int columnCount = 1; // Track the current column index
                    for (int i = 1; i < dataTable.Columns.Count + 1; i++)
                    {
                         if (dataTable.Columns[i - 1].ColumnName.Contains("button_") ||
                            dataTable.Columns[i - 1].ColumnName.Contains("RowId") ||
                            ContainsArabicLetters(dataTable.Columns[i - 1].ColumnName)) {
                            continue;   
                        }
                       
                        if (dataTable.Columns[i - 1].ColumnName.Contains("decimal_"))
                        {
                            dataTable.Columns[i - 1].ColumnName = "System.Field." + dataTable.Columns[i - 1].ColumnName.Replace("decimal_", "");
                        }
                            // Add the header the first time through 
                        if (rowCount == 2)
                        {
                            ws.Cells[1, columnCount].Value = _localResourceService.GetResource(dataTable.Columns[i - 1].ColumnName);
                        }

                        // Check if the column contains HTML tags
                        if (!ContainsHtmlTags(dr[i - 1].ToString()))
                        {
                            if (dataTable.Columns[i - 1].DataType == typeof(DateTime))
                                ws.Cells[rowCount, columnCount].Value = (dr[i - 1] as DateTime?)?.ToString("yyyy/MM/dd hh:mm:ss", CultureInfo.InvariantCulture);
                            else if (dataTable.Columns[i - 1].DataType == typeof(bool))
                            {
                                var boolValue = dr[i - 1] as bool?;
                                if (!boolValue.HasValue)
                                {
                                    columnDeleted++;
                                    ws.Cells[rowCount, columnCount].Value = "";
                                }
                                else
                                    ws.Cells[rowCount, columnCount].Value = boolValue.Value ? _localResourceService.GetResource("System.Field.Yes") : _localResourceService.GetResource("System.Field.No");
                            }
                            else
                            {
                                if (dataTable.Columns[i - 1].ColumnName.Contains("Percentage") && double.TryParse(dr[i - 1].ToString(), out double percentageValue))
                                {
                                    ws.Cells[rowCount, columnCount].Value = percentageValue / 100; // Convert 10 to 0.10
                                }
                                else
                                {
                                    // Ensure only decimal values use a dot separator
                                    if (dataTable.Columns[i - 1].DataType == typeof(Decimal) && !dataTable.Columns[i - 1].ColumnName.Contains("Percentage"))
                                    {
                                        ws.Cells[rowCount, columnCount].Value = Convert.ToDecimal(dr[i - 1]).ToString("F2", CultureInfo.InvariantCulture);
                                    }
                                    else
                                    {
                                        
                                        ws.Cells[rowCount, columnCount].Value = dr[i - 1].ToString();
                                        var lookup = GetLookupListFormDataTable(dataTable);
                                    }
                                }
                            }
                            ws.Cells[rowCount, columnCount].Style.HorizontalAlignment = alignment;
                            columnCount++; // Increment the column index
                        }
                        else
                        {
                            columnToBeDeleted++;
                        }
                    }
                }

                

                columnToBeDeleted = columnToBeDeleted / dataTable.Rows.Count;
                Color myColor = Color.FromArgb(102, 68, 38, 207);
                // Adjust the range to include only non-HTML columns
                range = ws.Cells[1, 1, rowCount, dataTable.Columns.Count - columnDeleted - columnToBeDeleted];
                range.AutoFitColumns();
                range.Style.Border.Left.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                range.Style.Border.Top.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                range.Style.Border.Right.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                range.Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;

                range = ws.Cells[1, 1, 1, dataTable.Columns.Count - columnDeleted - columnToBeDeleted];
                range.Style.Font.Bold = true;
                range.Style.Font.Color.SetColor(System.Drawing.Color.White);
                range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(myColor);

                var memoryStream = new MemoryStream();
                pkg.SaveAs(memoryStream);
                memoryStream.Position = 0;

                return memoryStream;
            }

        }
        
        private bool ContainsHtmlTags(string input)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(input, "<.*?>");
        }
        
        public byte[] ConvertSqlCommandToByte(SqlCommand cmd)
        {
            var keyValuePairs = new Dictionary<string, object?>();

            foreach (SqlParameter param in cmd.Parameters)
            {
                keyValuePairs[param.ParameterName] = param.Value;
            }

            var json = System.Text.Json.JsonSerializer.Serialize(keyValuePairs);
            return System.Text.Encoding.UTF8.GetBytes(json);
        }

        public SqlCommand GetSqlCommanFromByte(byte[] cmdByte)
        {
            var cmd = new SqlCommand();

            if (cmdByte != null)
            {
                var json = System.Text.Encoding.UTF8.GetString(cmdByte);
                var dictionary = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

                foreach (var kvp in dictionary)
                {
                    // Attempt to infer type from JsonElement
                    object value = GetValueFromJsonElement(kvp.Value);
                    cmd.Parameters.AddWithValue(kvp.Key, value ?? DBNull.Value);
                }
            }

            return cmd;
        }

        // Helper to convert JsonElement to .NET object
        private object? GetValueFromJsonElement(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number when element.TryGetInt32(out var i) => i,
                JsonValueKind.Number when element.TryGetDouble(out var d) => d,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.ToString(), // fallback for things like DateTime, etc.
            };
        }

        public byte[] DataTableToByteArray(DataTable table)
        {
            try { 
                table.TableName = "TableName";
                using var ms = new MemoryStream();
                var serializer = new DataContractSerializer(typeof(DataTable));
                serializer.WriteObject(ms, table);
                return ms.ToArray();
            }
            catch(Exception e)
            {
                throw e;
            }
        }

        public DataTable ByteArrayToDataTable(byte[] bytes)
        {
            try
            {
                using var ms = new MemoryStream(bytes);
                var serializer = new DataContractSerializer(typeof(DataTable));
                return (DataTable)serializer.ReadObject(ms);
            }
            catch (Exception e)
            {
                throw e;
            }
        }

        public List<Lookup> GetLookupListFormDataTable(DataTable dt)
        {
            var convertedList = (from rw in dt.AsEnumerable()
                                 where dt.Columns.Contains("Code") && dt.Columns.Contains("Name") &&
                                       rw["Code"] != DBNull.Value && rw["Name"] != DBNull.Value
                                 select new Lookup()
                                 {
                                     Code = rw["Code"].ToString(),
                                     Value = rw["Name"].ToString()
                                 }).ToList();

            return convertedList;
        }

        public void ThrowMessageAsException(string statusCode, string statusMessage) {
            var ex = new Exception(string.Format("{0} - {1}", statusMessage, statusCode));
            ex.Data.Add(statusCode, statusMessage);  
            throw ex;
        }

        internal object? GetFieldDefaultValue(List<Fields> fields, object defaultValue)
        {
            if (! defaultValue.ToString().Contains("["))
            {
                return defaultValue;
            }

            foreach (var row in fields)
            {
                if (row.Name.ToLower() == defaultValue.ToString().Replace("[", "").Replace("]", "").ToLower())
                {
                    return row.Value;
                }
            }
            return "";
        }

        public static string ReplaceCaseInsensitive(string input, string search, string replacement)
        {
            string result = Regex.Replace(
                input,
                Regex.Escape(search),
                replacement.Replace("$", "$$"),
                RegexOptions.IgnoreCase
            );
            return result;
        }
        
        public async Task<bool> UserSession(Users user, string sessionId, int source, string sessionKey)
        {
            var httpContext = _httpContextAccessor.HttpContext;
            bool isLocalhost = httpContext.Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                               || IPAddress.IsLoopback(httpContext.Connection.RemoteIpAddress);

            string ipAddress = isLocalhost ? "94.249.3.50" : httpContext.Connection.RemoteIpAddress.ToString();
            var locationResponse = JsonConvert.DeserializeObject<LocationResponse>(GetLocation(ipAddress));

            // Determine device model
            string deviceModel = null;
            if (source == 2)
            {
                var userAgent = httpContext.Request.Headers["User-Agent"].ToString().ToLower();
                if (userAgent.Contains("android"))
                {
                    deviceModel = "Android";
                }
                else if (userAgent.Contains("iphone"))
                {
                    deviceModel = "iPhone";
                }
                else
                {
                    deviceModel = "Unknown";
                }
            }

            var sessionUser = new SignedInUsers
            {
                Created = DateTime.Now,
                CreatedBy = user.Id,
                LastUpd = DateTime.Now,
                IPAddress = ipAddress,
                Location = locationResponse.latitude + "," + locationResponse.longitude,
                Country = locationResponse.country_name,
                Region = locationResponse.country_capital,
                Active = true,
                SessionId = sessionId,
                Source = source,
                SessionKey = sessionKey,
                DeviceModel = deviceModel
            };

            var sessionUserId = _domainRepo.CreateSignInUser(sessionUser);
            return sessionUserId > 0;
        }

        public static string GetLocation(string ip)
        {
            var res = "";
            WebRequest request = WebRequest.Create("https://api.ipgeolocation.io/ipgeo?apiKey=74b0f6256f674dfc9568c86e88c59680&ip=" + ip);
            WebResponse response = request.GetResponse();
            StreamReader stream = new StreamReader(response.GetResponseStream());
            string line;
            while ((line = stream.ReadLine()) != null)
            {
                res += line;
            }
            
            return res;
        }
       
        public string GetEmailTemplate(string EmailTeamplateName)
        {
            try { 
                var lang = Thread.CurrentThread.CurrentUICulture.Name;
                string EmailFolder = (lang == "en" || lang == "en-US") ? "English" : "Arabic"; 
                var rootPath = _webHostEnvironment.WebRootPath;

                var templatePath = rootPath + Path.DirectorySeparatorChar.ToString() + "EmailTemplates" + Path.DirectorySeparatorChar.ToString()
                // var templatePath = "c:\\EmailTemplates" + Path.DirectorySeparatorChar.ToString()
                                + EmailFolder + Path.DirectorySeparatorChar.ToString() + EmailTeamplateName + ".html";
                string HtmlBody = "";
                using (StreamReader streamReader = System.IO.File.OpenText(templatePath))
                {
                    HtmlBody = streamReader.ReadToEnd();
                }
                return HtmlBody;
            }
            catch (Exception e) {
                return _webHostEnvironment.WebRootPath;
            }
        }
        
        public string IGetSettingValue(string code)
        {
            try
            {
                string queryText = "";
                string value = "";
                SqlCommand cmd = new SqlCommand();

                cmd.Parameters.AddWithValue("@Code", code);

                queryText = "Select [dbo].[GetSettingVal](@Code)";
                value = ExecuteQuery_OneValue(queryText, cmd, getConnectionString());
                return value;
            }
            catch (Exception err)
            {
                return getExceptionErrorMessage(err);
            }
        }
        
        bool ContainsArabicLetters(string input)
        {
            foreach (char c in input)
            {
                if (c >= '\u0600' && c <= '\u06FF')
                {
                    return true;
                }
            }
            return false;
        }
        public List<Dictionary<string, object>> ExecuteListDictionary(string sql, SqlCommand cmd, string connectionString)
        {
            var result = new List<Dictionary<string, object>>();

            using (var conn = new SqlConnection(connectionString))
            {
                cmd.Connection = conn;
                cmd.CommandText = sql;

                conn.Open();
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var row = new Dictionary<string, object>();

                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            string col = reader.GetName(i);
                            object val = reader.IsDBNull(i) ? null : reader.GetValue(i);
                            row[col] = val;
                        }

                        result.Add(row);
                    }
                }
            }

            return result;
        }
        public string BuildSqlColumn(string expr, LoadDataFieldDto f)
        {
            return f.DataType switch
            {
                "Date" => $"convert(varchar, {expr}, 20) {f.FieldName},",
                "DateTime" => $"CONVERT(VARCHAR, FORMAT({expr}, 'dd/MM/yyyy hh:mm tt')) {f.FieldName},",
                "Time" => $"CONVERT(VARCHAR, FORMAT({expr}, 'hh:mm tt')) {f.FieldName},",
                "Color" => $"{expr} color_{f.FieldName},",
                "CheckBox" => $"{expr} check_{f.FieldName},",
                "Decimal" => $"{expr} decimal_{f.FieldName},",
                "InnerButton" => $"{expr} button_{f.FieldName},",
                "Button" => $"{expr} button_{f.FieldName},",
                "File" => $"{expr} file_{f.FieldName},",
                _ => $"{expr} {f.FieldName},"
            };
        }
        public string BuildSqlSearch(KeyValuePair<string, string> item, LoadDataFieldDto f, SqlCommand cmd)
        {
            string key = item.Key;
            string val = item.Value;

            bool isDate = key.EndsWith("_From") || key.EndsWith("_To");
            string baseKey = isDate ? key.Split('_')[0] : key;

            string column = f.IsCalc ? f.CalcExpression : $"{f.TableName}.{f.TableColumn}";
            string sql = "";

            if (isDate)
            {
                sql = $"{column} {(key.EndsWith("_From") ? ">=" : "<=")} @{key}";
                cmd.Parameters.AddWithValue($"@{key}", val);
            }
            else if (f.DataType.ToLower() == "text")
            {
                sql = $"{column} LIKE @{key}";
                cmd.Parameters.AddWithValue($"@{key}", $"%{val}%");
            }
            else if (f.DataType.ToLower() == "checkbox")
            {
                sql = $"{column} = @{key}";
                cmd.Parameters.AddWithValue($"@{key}", val == "true" ? "1" : "0");
            }
            else
            {
                sql = $"{column} = @{key}";
                cmd.Parameters.AddWithValue($"@{key}", val);
            }

            return (sql + " AND ");
        }
        #endregion
    }
}


