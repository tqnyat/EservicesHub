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
using System.Text;
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
        public DataShape ExecuteQuery_DataShape(string query, SqlCommand cmd, string connectionString = null, CommandType commandType = CommandType.Text)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                connectionString = getConnectionString();

            using var con = new SqlConnection(connectionString);

            cmd.Connection = con;
            cmd.CommandType = commandType;

            if(!string.IsNullOrEmpty(query))
                cmd.CommandText = query;

            con.Open();
            using var reader = cmd.ExecuteReader();

            var shape = new DataShape();

            // Build columns
            for (int i = 0; i < reader.FieldCount; i++)
            {
                shape.Columns.Add(new ColumnShape
                {
                    Name = reader.GetName(i),
                    DataType = reader.GetFieldType(i).Name
                });
            }

            // Build rows
            while (reader.Read())
            {
                var row = new Dictionary<string, object>();

                for (int i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);

                shape.Rows.Add(row);
            }

            return shape;
        }

        public int ExecuteNonQuerySafe(string sql, SqlCommand cmd, string connectionString)
        {
            using var con = new SqlConnection(connectionString);
            con.Open();

            cmd.Connection = con;
            cmd.CommandText = sql;
            cmd.CommandType = CommandType.Text;

            return cmd.ExecuteNonQuery();
        }

        public async Task<Dictionary<string, object>> ExecuteSingleRow(string sql, SqlCommand cmd, string connectionString = null)
        {
            if (cmd == null)
                cmd = new SqlCommand();

            if (string.IsNullOrEmpty(connectionString))
                connectionString = getConnectionString();

            var result = new Dictionary<string, object>();

            using (var conn = new SqlConnection(connectionString))
            {
                cmd.Connection = conn;
                cmd.CommandText = sql;
                cmd.CommandType = CommandType.Text;

                await conn.OpenAsync();
                using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow);

                if (await reader.ReadAsync())
                {
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        string name = reader.GetName(i);
                        object value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        result[name] = value;
                    }
                }
            }

            return result;
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

        public List<Lookup> GetLookupListFormDataShape(DataShape shape)
        {
            var list = new List<Lookup>();

            if (shape == null || shape.Rows == null || shape.Rows.Count == 0)
                return list;

            foreach (var row in shape.Rows)
            {
                // must contain both Code and Name
                if (!row.ContainsKey("Code") || !row.ContainsKey("Name"))
                    continue;

                var code = row["Code"];
                var name = row["Name"];

                // skip null / DBNull
                if (code == null || code == DBNull.Value ||
                    name == null || name == DBNull.Value)
                    continue;

                list.Add(new Lookup
                {
                    Code = code.ToString(),
                    Value = name.ToString()
                });
            }

            return list;
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
        
        public List<Dictionary<string, object>> ExecuteListDictionary(string sql, SqlCommand cmd, string connectionString)
        {
            var result = new List<Dictionary<string, object>>();

            // -----------------------------
            // FIX 1: Auto-correct bad SQL spacing
            // -----------------------------
            sql = FixSql(sql);

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
        private string FixSql(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
                return sql;

            // Remove double spaces
            sql = Regex.Replace(sql, @"\s{2,}", " ");

            // Ensure WHERE has space before it
            sql = Regex.Replace(sql, @"([A-Za-z0-9_])Where", "$1 WHERE", RegexOptions.IgnoreCase);

            // Ensure ORDER BY has space
            sql = Regex.Replace(sql, @"([A-Za-z0-9_])Order\s+By", "$1 ORDER BY", RegexOptions.IgnoreCase);

            // Ensure OFFSET has space
            sql = Regex.Replace(sql, @"([A-Za-z0-9_])OFFSET", "$1 OFFSET", RegexOptions.IgnoreCase);

            // Ensure FETCH has space
            sql = Regex.Replace(sql, @"([A-Za-z0-9_])FETCH", "$1 FETCH", RegexOptions.IgnoreCase);

            // Remove "WHERE AND"
            sql = Regex.Replace(sql, @"WHERE\s+AND", "WHERE", RegexOptions.IgnoreCase);

            // Remove trailing AND
            sql = Regex.Replace(sql, @"AND\s*$", "", RegexOptions.IgnoreCase);

            // Remove trailing WHERE
            sql = Regex.Replace(sql, @"WHERE\s*$", "", RegexOptions.IgnoreCase);

            return sql.Trim();
        }

        #endregion
    }
}


