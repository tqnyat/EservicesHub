using DomainServices.Data;
using DomainServices.Data.Repository;
using DomainServices.Models;
using DomainServices.Models.Core;
using DomainServices.Services.Interfaces;
using Ganss.Xss;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using NLog.Config;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;
using Component = DomainServices.Models.Component;


namespace DomainServices.Services
{
    public class CoreServices : ControllerBase
    {
        private readonly UserManager<Users> _userManager;
        private readonly CommonServices _commonServices;
        private readonly ILocalResourceService _localResourceService;
        private readonly CoreData _coreData;
        private readonly DomainDBContext.DomainRepo _domainRepo;
        private readonly SignInManager<Users> _signInManager;

        public CoreServices(
            UserManager<Users> userManager,
            CommonServices commonServices,
            ILocalResourceService localResourceService,
            CoreData coreData,
            DomainDBContext.DomainRepo domainRepo,
            SignInManager<Users> signInManager)
        {
            _userManager = userManager;
            _commonServices = commonServices;
            _localResourceService = localResourceService;
            _coreData = coreData;
            _domainRepo = domainRepo;
            _signInManager = signInManager;
        }
        #region Methods

        public Dictionary<string, object> IsAuthenticated(ClaimsPrincipal currentUser)
        {
            if (currentUser == null || !currentUser.Identity.IsAuthenticated)
            {
                _commonServices.ThrowMessageAsException("not Autherized or Session timeout", "401");
            }

            var user = _userManager.GetUserAsync(currentUser).Result;

            string queryString = "SELECT FirstName + ' ' + LastName AS FULL_NAME, NULL AS PROFILE_IMAGE, 'Client' USER_TYPE, (SELECT NAME FROM Groups WHERE ID = UserGroupId) AS CompanyName FROM [dbo].AspNetUsers U WHERE U.Id = @UserId";
            SqlCommand cmd = new SqlCommand();
            cmd.Parameters.AddWithValue("@UserId", user.Id);
            DataTable dt = _commonServices.getDataTableFromQuery(queryString, cmd, _commonServices.getConnectionString(), CommandType.Text);

            return new Dictionary<string, object> { { "UserInfo", dt }, { "UserName", user.UserName } };
        }

        public async Task<GetUserViewsResponse> IGetUserViewsAsync(ClaimsPrincipal currentUser)
        {
            if (currentUser == null || !currentUser.Identity.IsAuthenticated)
            {
                _commonServices.ThrowMessageAsException("not Autherized or Session timeout", "401");
            }
            var username = currentUser.FindFirst("username").Value;
            var user = await _userManager.FindByNameAsync(username);

            string userFullName = $"{user.FirstName} {user.LastName}";
            string applicationTheme = user.ApplicationTheme;

            string query = @"
                SELECT DISTINCT 
                    V.Id, 
                    V.Type, 
                    V.ViewStyle, 
                    V.Name, 
                    V.Title, 
                    V.ViewSequence, 
                    V.MainCategory, 
                    V.ViewIcon, 
                    PD.ReadOnly
                FROM RolePermissions RP
                INNER JOIN Permissions P ON RP.PermissionId = P.Id
                INNER JOIN PermissionDetails PD ON P.Id = PD.ParentId
                INNER JOIN Views V ON PD.ViewId = V.Id
                WHERE RP.RoleId = @RoleId
                ORDER BY V.ViewSequence;
            ";

            var list = new List<ViewListItemDto>();

            using (SqlConnection conn = new SqlConnection(_commonServices.getConnectionString()))
            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.Add("@RoleId", SqlDbType.Int).Value = user.RoleId;

                await conn.OpenAsync();
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var item = new ViewListItemDto
                        {
                            Id = reader.GetDecimal(reader.GetOrdinal("Id")),
                            Type = reader.GetDecimal(reader.GetOrdinal("Type")),
                            ViewStyle = reader.IsDBNull(reader.GetOrdinal("ViewStyle"))
                                ? (int?)null
                                : reader.GetInt32(reader.GetOrdinal("ViewStyle")),
                            Name = reader.GetString(reader.GetOrdinal("Name")),
                            Title = _localResourceService.GetResource(reader.GetString(reader.GetOrdinal("Title"))),
                            ViewSequence = reader.GetDecimal(reader.GetOrdinal("ViewSequence")),
                            MainCategory = reader.IsDBNull(reader.GetOrdinal("MainCategory"))
                                ? (decimal?)null
                                : reader.GetDecimal(reader.GetOrdinal("MainCategory")),
                            ViewIcon = reader.GetString(reader.GetOrdinal("ViewIcon")),
                            ReadOnly = reader.GetBoolean(reader.GetOrdinal("ReadOnly"))
                        };

                        list.Add(item);
                    }
                }
            }

            return new GetUserViewsResponse
            {
                ListDetial = list,
                UserFullName = userFullName,
                ApplicationTheme = applicationTheme
            };
        }

        public async Task<LoadViewResponse> ILoadView(Dictionary<string, string> t, ClaimsPrincipal currentUser)
        {
            if (currentUser?.Identity?.IsAuthenticated != true)
                _commonServices.ThrowMessageAsException("not authorized or session timeout", "401");

            if (!t.TryGetValue("viewName", out var viewName) || string.IsNullOrWhiteSpace(viewName))
                throw new Exception("viewName is required");
            var username = currentUser.FindFirst("username").Value;
            var user = await _userManager.FindByNameAsync(username);

            var currentLang = Thread.CurrentThread.CurrentCulture.Name.ToLower();
            var lang = currentLang.Contains("ar") ? "ar" : "en";

            string sqlView = @"
                SELECT DISTINCT 
                    VC.Seq, V.ClientURL, V.Title AS ViewTitle, VC.ViewId, V.ViewStyle,
                    VC.ViewDataAccess, C.Id AS CompId, C.Name AS CompName, C.Title AS CompTitle,
                    VC.CompFieldId, VC.ParCompId, VC.ParCompFieldId,
                    PD.ReadOnly, C.ExportExcel, VC.NoInsert, VC.NoUpdate, VC.NoDelete,
                    CF.Name AS CompFieldName, PC.Name AS ParCompName, PCF.Name AS ParCompFieldName,
                    CASE WHEN EXISTS (
                        SELECT 1 FROM ViewComponent VCC 
                        WHERE VCC.ParCompId = VC.CompId AND VCC.ViewId = VC.ViewId
                    ) THEN 1 ELSE 0 END AS HasDetail,
                    CASE WHEN @Lang = 'ar' THEN V.DescriptionAr ELSE V.DescriptionEn END AS ViewDescription
                FROM ViewComponent VC
                JOIN Views V ON V.Id = VC.ViewId
                JOIN Component C ON C.Id = VC.CompId
                JOIN RolePermissions RP ON RP.RoleId = @RoleId
                JOIN Permissions P ON P.Id = RP.PermissionId
                JOIN PermissionDetails PD ON PD.ParentId = P.Id AND PD.ViewId = V.Id
                LEFT JOIN ComponentField CF ON VC.CompFieldId = CF.Id
                LEFT JOIN Component PC ON VC.ParCompId = PC.Id
                LEFT JOIN ComponentField PCF ON VC.ParCompFieldId = PCF.Id
                WHERE V.Name = @ViewName
                ORDER BY VC.Seq;
            ";

            var list = new List<LoadViewListItemDto>();
            string viewDescription = "";

            using (var conn = new SqlConnection(_commonServices.getConnectionString()))
            using (var cmd = new SqlCommand(sqlView, conn))
            {
                cmd.Parameters.AddWithValue("@RoleId", user.RoleId);
                cmd.Parameters.AddWithValue("@ViewName", viewName);
                cmd.Parameters.AddWithValue("@Lang", lang);

                await conn.OpenAsync();
                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var item = new LoadViewListItemDto
                    {
                        Seq = reader.GetDecimal("Seq"),
                        ClientURL = reader["ClientURL"]?.ToString() ?? "",
                        ViewTitle = _localResourceService.GetResource(reader["ViewTitle"]?.ToString() ?? "") ?? "",
                        ViewId = reader.GetDecimal("ViewId"),
                        ViewStyle = reader.GetInt32("ViewStyle"),
                        ViewDataAccess = reader.GetInt32("ViewDataAccess"),
                        CompId = reader.GetDecimal("CompId"),
                        CompName = reader["CompName"]?.ToString() ?? "",
                        CompTitle = _localResourceService.GetResource(reader["CompTitle"]?.ToString() ?? "") ?? "",

                        CompFieldId = reader["CompFieldId"] as decimal?,
                        ParCompId = reader["ParCompId"] as decimal?,
                        ParCompFieldId = reader["ParCompFieldId"] as decimal?,

                        ReadOnly = reader.GetBoolean("ReadOnly"),
                        ExportExcel = reader.GetBoolean("ExportExcel"),
                        NoInsert = reader.GetBoolean("NoInsert"),
                        NoUpdate = reader.GetBoolean("NoUpdate"),
                        NoDelete = reader.GetBoolean("NoDelete"),

                        CompFieldName = reader["CompFieldName"]?.ToString() ?? "",
                        ParCompName = reader["ParCompName"]?.ToString() ?? "",
                        ParCompFieldName = reader["ParCompFieldName"]?.ToString() ?? "",

                        HasDetail = reader.GetInt32("HasDetail"),
                        ViewDescription = reader["ViewDescription"]?.ToString() ?? ""
                    };

                    viewDescription = item.ViewDescription;
                    list.Add(item);
                }
            }

            var componentNames = list.Select(x => x.CompName).Distinct().ToList();

            var componentMap = await LoadAllComponentDefinitions(componentNames);

            // attach metadata
            foreach (var item in list)
            {
                if (componentMap.TryGetValue(item.CompName, out var def))
                    item.Component = def;
            }

            var json = JsonConvert.SerializeObject(list);
            var bytes = Encoding.UTF8.GetBytes(json);
            var base64 = Convert.ToBase64String(bytes);

            return new LoadViewResponse
            {
                ListDetial = list,
                ViewDescription =  viewDescription,
                UpdatedSession =  new Dictionary<string, object> {
                    { "CoreDataView", base64 }
                }
            };
        }

        public async Task<ListDetialResponse> IGetTemplate(Dictionary<string, string> t, ClaimsPrincipal currentUser)
        {
            if (currentUser?.Identity?.IsAuthenticated != true)
                _commonServices.ThrowMessageAsException("not Authorized or Session timeout", "401");

            if (!t.TryGetValue("componentName", out var componentName) || string.IsNullOrWhiteSpace(componentName))
                throw new Exception("componentName is required");

            string sql = @"
                SELECT C.Name AS ComponentName, C.Title AS ComponentTitle, C.RowsCount AS PageSize, CF.*
                FROM Component C
                JOIN ComponentField CF ON C.Id = CF.ComponentId
                WHERE C.Name = @ComponentName
                ORDER BY CF.DisplaySequence;
            ";

            var list = new List<ListDetialDto>();

            using (var conn = new SqlConnection(_commonServices.getConnectionString()))
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@ComponentName", componentName);

                await conn.OpenAsync();
                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var dto = new ListDetialDto
                    {
                        ComponentName = reader["ComponentName"]?.ToString() ?? "",
                        ComponentTitle = _localResourceService.GetResource(reader["ComponentTitle"]?.ToString() ?? ""),
                        PageSize = reader["PageSize"] == DBNull.Value ? 10 : Convert.ToInt32(reader["PageSize"]),

                        Id = Convert.ToInt32(reader["Id"]),
                        Created = Convert.ToDateTime(reader["Created"]),

                        CreatedBy = reader["CreatedBy"] == DBNull.Value ? null : reader["CreatedBy"].ToString(),
                        LastUpd = reader["LastUpd"] == DBNull.Value ? null : Convert.ToDateTime(reader["LastUpd"]),
                        LastUpdBy = reader["LastUpdBy"] == DBNull.Value ? null : reader["LastUpdBy"].ToString(),
                        GroupId = reader["GroupId"] == DBNull.Value ? null : Convert.ToDecimal(reader["GroupId"]),

                        Name = reader["Name"].ToString(),
                        TableName = reader["TableName"] == DBNull.Value ? null : reader["TableName"].ToString(),
                        Lable = _localResourceService.GetResource(reader["Lable"]?.ToString() ?? ""),
                        ColumnName = reader["ColumnName"].ToString(),
                        DataType = reader["DataType"].ToString(),
                        DefaultValue = reader["DefaultValue"] == DBNull.Value ? null : reader["DefaultValue"].ToString(),

                        Required = Convert.ToBoolean(reader["Required"]),
                        ReadOnly = Convert.ToBoolean(reader["ReadOnly"]),
                        DisplayInList = Convert.ToBoolean(reader["DisplayInList"]),
                        DisplayInForm = Convert.ToBoolean(reader["DisplayInForm"]),
                        Comment = reader["Comment"] == DBNull.Value ? null : reader["Comment"].ToString(),

                        ComponentId = Convert.ToInt32(reader["ComponentId"]),
                        DisplaySequence = Convert.ToDecimal(reader["DisplaySequence"]),

                        IsCalc = Convert.ToBoolean(reader["IsCalc"]),
                        CalcExpr = reader["CalcExpr"] == DBNull.Value ? null : reader["CalcExpr"].ToString(),
                        HtmlStyle = reader["HtmlStyle"] == DBNull.Value ? null : reader["HtmlStyle"].ToString(),
                        LookUp = reader["LookUp"] == DBNull.Value ? null : Convert.ToDecimal(reader["LookUp"]),
                        ImmediatePost = Convert.ToBoolean(reader["ImmediatePost"]),
                        DisplayInPopup = Convert.ToBoolean(reader["DisplayInPopup"]),
                        FileDataColumn = reader["FileDataColumn"] == DBNull.Value ? null : reader["FileDataColumn"].ToString(),
                        FieldSize = reader["FieldSize"] == DBNull.Value ? null : Convert.ToInt32(reader["FieldSize"])
                    };

                    list.Add(dto);
                }
            }

            return new ListDetialResponse { ListDetial = list };
        }

        public async Task<object> ISubmitData(Dictionary<string, object> t, ClaimsPrincipal currentUser, ISession session)
        {
            if (currentUser == null || !currentUser.Identity.IsAuthenticated)
            {
                _commonServices.ThrowMessageAsException("not Autherized or Session timeout", "401");
            }

            #region Declare Variables

            // declare variables
            var componentName = t["componentName"].ToString();
            var submitedData = JsonConvert.DeserializeObject<Dictionary<string, object>>(t["dataList"].ToString());
            var selectedId = t["selectedId"].ToString();
            var saveType = t["saveType"].ToString();

            var queryText = "";
            var tableColumns = "";
            var tableValues = "";
            var parentField = "1";
            var parentFieldValue = "1";
            var connectionString = "";
            var hasGroupId = false;
            string rowId = "0";
            object value = "";


            // define objects
            var user = _userManager.GetUserAsync(currentUser).Result;
            var coreDataView = _commonServices.ByteArrayToDataTable(session.Get("CoreDataView"));
            var cmd = new SqlCommand();
            var component = new Component();
            var componentDetailList = new List<ComponentDetail>();
            DataRow[] dataRow;
            List<LoadViewListItemDto> da = null;
            dataRow = coreDataView.Select("CompName = '" + componentName + "'");
            parentField = dataRow[0]["CompFieldName"].ToString();
            parentFieldValue = dataRow[0]["ParCompFieldValue"].ToString();
            var sessionFields = session.GetString("Fields" + componentName + selectedId);
            var fields = (sessionFields == null || t["selectedId"] == "-1") ?
                _coreData.InitFields(da, componentName, selectedId, user) :
                JsonConvert.DeserializeObject<List<Fields>>(sessionFields);
            component = JsonConvert.DeserializeObject<Component>(dataRow[0]["Component"].ToString());

            #endregion

            if (saveType == "new" && dataRow[0]["NoInsert"] != null && dataRow[0]["NoInsert"].ToString() == "1")
            {
                return new Dictionary<string, object> { { "ExceptionError", "لا تملك صلاحية الاضافة" } };
            }

            if (saveType == "edit" && dataRow[0]["NoUpdate"] != null && dataRow[0]["NoUpdate"].ToString() == "1")
            {
                return new Dictionary<string, object> { { "ExceptionError", "لا تملك صلاحية التعديل" } };
            }

            foreach (var row in fields)
            {
                if ((!Convert.ToBoolean(row.ReadOnly) && Convert.ToBoolean(row.Visible)
                    || (row.DefaultValue != null && row.DefaultValue.ToString() != "" && saveType == "new"))
                    && row.Type.ToString() != "Button" && !row.IsCalc)
                {
                    submitedData.TryGetValue(_commonServices.GetValue(row.Name), out var val);
                    row.Value = (val == null) ? row.Value : val;

                    if (row.Required && (row.Value == null || row.Value.ToString() == ""))
                    {
                        throw new Exception($"column {row.Name} is Required", new Exception(row.Name));
                    }

                    if ((row.Value == null || row.Value.ToString() == "")
                        && (row.DefaultValue != null && row.DefaultValue.ToString() != ""))
                    {
                        row.Value = _commonServices.GetFieldDefaultValue(fields, row.DefaultValue);
                    }
                    if (row.Value != null && CheckHtmlContent(row.Value.ToString()))
                    {
                        if (user?.RoleId != 1 && componentName != "SysView")
                        {
                            throw new Exception("Contains Html Content");
                        }
                    }
                    if (row.Value != null && row.Value.ToString() != "")
                    {
                        if (row.Type.ToString().ToLower() == "date" || row.Type.ToString().ToLower() == "datetime" || row.Type.ToString().ToLower() == "time")
                        {
                            string dateString, format = "";
                            DateTime result;
                            CultureInfo provider = CultureInfo.InvariantCulture;
                            submitedData.TryGetValue(_commonServices.GetValue(row.Name), out var rowVal);
                            rowVal = rowVal == null ? row.Value : rowVal;
                            dateString = rowVal.ToString();

                            if (row.Type.ToString().ToLower() == "date")
                            {
                                format = "dd/MM/yyyy";
                            }
                            else if (row.Type.ToString().ToLower() == "datetime")
                            {
                                format = "yyyy-MM-ddTHH:mm";
                            }
                            else if (row.Type.ToString().ToLower() == "time")
                            {
                                format = "hh:mm tt";
                            }

                            result = DateTime.ParseExact(dateString, format, provider);

                            row.Value = result;
                        }
                        else if (row.Type.ToString().ToLower() == "checkbox")
                        {
                            submitedData.TryGetValue(_commonServices.GetValue(row.Name), out var rowVal);
                            row.Value = rowVal == null ? Convert.ToBoolean(row.Value) : Convert.ToBoolean(rowVal);
                        }
                        else if (row.Type.ToString().ToLower() == "file")// manage Files
                        {
                            if (submitedData.TryGetValue(_commonServices.GetValue(row.Name), out var fileValue))
                            {
                                row.Value = fileValue;
                                row.FileDataValue = submitedData[row.Name + "_data"].ToString();
                            }
                        }
                        else
                        {
                            submitedData.TryGetValue(_commonServices.GetValue(row.Name), out var rowVal);
                            row.Value = rowVal == null ? row.Value : rowVal;
                        }
                    }

                    if (saveType == "new")
                    {
                        if (row.ColumnName.ToLower() == "groupid")
                        {
                            hasGroupId = true;
                        }

                        if (row.Type.ToString().ToLower() == "file") // manage Files
                        {
                            if (!String.IsNullOrEmpty(row.FileDataColumn?.ToString()))
                            {
                                if (row.FileDataColumn == "clear-old-data")
                                {
                                    tableColumns += "'',";
                                    tableValues += "@" + row.Name + ",";

                                    tableColumns += "'',";
                                    tableValues += "@" + row.FileDataColumn + ",";
                                }
                                else
                                {
                                    tableColumns += row.ColumnName + ",";
                                    tableValues += "@" + row.Name + ",";

                                    tableColumns += row.FileDataColumn + ",";
                                    tableValues += "@" + row.FileDataColumn + ",";
                                }
                            }

                        }
                        else
                        {
                            tableColumns += row.ColumnName + ",";
                            tableValues += "@" + row.Name + ",";
                        }
                    }
                    else
                    {
                        if (row.Type.ToString().ToLower() == "file" && row.Value != null && row.Value.ToString() != "")
                        {
                            if (!string.IsNullOrEmpty(row.FileDataValue) && row.FileDataValue != "clear-old-file")
                            {
                                tableColumns += row.ColumnName + "=" + "@" + row.Name + ",";
                                tableColumns += row.FileDataColumn + "=" + "@" + row.FileDataColumn + ",";
                                byte[] fileBytes = Convert.FromBase64String(row.FileDataValue.ToString());
                                //cmd.Parameters.AddWithValue("@" + row.FileDataColumn, fileBytes);
                            }
                            else
                            {
                                tableColumns += row.ColumnName + "=" + "@" + row.Name + ",";
                            }
                        }
                        else if (row.Type.ToString().ToLower() != "file")
                        {
                            tableColumns += row.ColumnName + "=" + "@" + row.Name + ",";
                        }
                    }

                    cmd.Parameters.AddWithValue("@" + row.Name, (row.Value == null || row.Value.ToString() == "") ? DBNull.Value : row.Value);

                    if (row.Type.ToString().ToLower() == "file") // manage Files
                    {
                        byte[] fileBytes = (row.FileDataValue == "clear-old-file") ? new byte[0] : Convert.FromBase64String(row.FileDataValue.ToString());
                        cmd.Parameters.AddWithValue("@" + row.FileDataColumn, fileBytes);
                    }
                }
                if (row.Name.ToString() == parentField)
                    parentField = row.ColumnName.ToString();
            }

            string guid = Guid.NewGuid().ToString();
            if (saveType == "new")
            {
                if (parentField != "1" && parentFieldValue != "" && parentFieldValue != null)
                {
                    tableColumns += parentField + ",";
                    tableValues += "'" + parentFieldValue + "',";

                    if (parentField.ToLower() == "groupid")
                    {
                        hasGroupId = true;
                    }
                }

                tableColumns += component.TableName.ToLower() == "aspnetusers" ? "Id," : "";

                tableColumns += "CreatedBy, LastUpdBy";
                tableColumns += hasGroupId ? "" : ", GroupId";
                tableColumns += component.TableName.ToLower() == "aspnetusers" ? ", UserGroupId, SecurityStamp" : "";

                tableValues += component.TableName.ToLower() == "aspnetusers" ? $"'{guid}'," : "";
                tableValues += "'" + user.Id + "', '" + user.Id + "'";
                tableValues += hasGroupId ? "" : "," + user.UserGroupId;
                tableValues += component.TableName.ToLower() == "aspnetusers" ? $", {user.UserGroupId} ,'{guid}'" : "";

                queryText = "insert into " + component.TableName + "(" + tableColumns.TrimEnd(',') + ") Values (" + tableValues.TrimEnd(',') + ")";
            }
            else
            {
                tableColumns += "LastUpdBy = '" + user.Id + "', LastUpd = GetDate()";
                queryText = "Update " + component.TableName + " Set " + tableColumns.TrimEnd(',') + " Where Id = @SelectedId";
                cmd.Parameters.AddWithValue("@SelectedId", selectedId);
            }
            connectionString = _commonServices.getConnectionString();

            SqlTransaction sqlTrans = null;
            SqlConnection con = new SqlConnection(connectionString);
            try
            {
                con.Open();
                sqlTrans = con.BeginTransaction(IsolationLevel.ReadUncommitted);
                rowId = _commonServices.ExecuteQuery_OneValue_Trans(queryText + "; SELECT scope_identity(); ", ref con, ref sqlTrans, cmd, CommandType.Text);
                rowId = (rowId == "") ? selectedId : rowId;

                if (saveType == "new")
                {
                    rowId = component.TableName.ToLower() == "aspnetusers" ? guid : rowId;
                    if (component.OnCreateProc != null && component.OnCreateProc != "")
                    {
                        cmd.Parameters.Clear();
                        cmd.Parameters.AddWithValue("@RowId", rowId);
                        int x = _commonServices.excuteQuery_Trans(component.OnCreateProc, ref con, ref sqlTrans, cmd, CommandType.StoredProcedure);
                    }
                }
                else
                {
                    if (component.OnUpdateProc != null && component.OnUpdateProc != "")
                    {
                        cmd.Parameters.Clear();
                        cmd.Parameters.AddWithValue("@RowId", rowId);
                        int x = _commonServices.excuteQuery_Trans(component.OnUpdateProc, ref con, ref sqlTrans, cmd, CommandType.StoredProcedure);

                    }
                }

                sqlTrans.Commit();
                session.SetString("Fields" + componentName + rowId, JsonConvert.SerializeObject(fields));
            }
            catch (Exception e)
            {
                sqlTrans.Rollback();
                throw;
            }
            finally
            {
                if (con != null && con.State != ConnectionState.Closed)
                    con.Close();
            }

            if (componentName == "CompanyUsers")
            {
                return new Dictionary<string, object> { { "RowId", rowId }, { "saveType", saveType } };
            }
            return new Dictionary<string, object> { { "RowId", rowId } };
        }

        public async Task<LoadDataResultDto> ILoadData(LoadDataRequest t, ClaimsPrincipal currentUser)
        {
            // -----------------------------------------------
            // 1. Validate User
            // -----------------------------------------------
            if (currentUser?.Identity?.IsAuthenticated != true)
                _commonServices.ThrowMessageAsException("not Authorized or Session timeout", "401");

            string componentName = t.ComponentName;
            int pageNumber = t.PageNumber.ToString() == "-1" ? -1 : Convert.ToInt32(t.PageNumber);
            string activeRow = string.IsNullOrEmpty(t.SelectedId) ? "-1" : t.SelectedId;

            var search = t.Search;
            var username = currentUser.FindFirst("username").Value;
            var user = _userManager.FindByNameAsync(username).Result;

            // -----------------------------------------------
            // 2. Decode CoreDataView (DTO list)
            // -----------------------------------------------
            string base64 = t.CoreDataView;
            byte[] bytes = Convert.FromBase64String(base64);
            string json = Encoding.UTF8.GetString(bytes);

            var coreViewList =
                JsonConvert.DeserializeObject<List<LoadViewListItemDto>>(json);

            // -----------------------------------------------
            // 3. Find the View Component
            // -----------------------------------------------
            var view = coreViewList.First(x => x.CompName == componentName);

            var component = view.Component; // strongly typed
            string parentField = view.CompFieldName ?? "";
            string parentFieldValue = view.ParCompFieldValue ?? "";
            int viewDataAccess = view.ViewDataAccess;

            string connectionString = _commonServices.getConnectionString();

            // we will reuse this SqlCommand
            SqlCommand cmd = new SqlCommand();

            // -----------------------------------------------
            // 4. PAGE NUMBER CALCULATION
            // -----------------------------------------------
            if (pageNumber == -1)
            {
                var pageNumberQuery = "SELECT PageNumber FROM (SELECT((ROW_NUMBER() OVER(ORDER BY " + component.SortSpec + ")) / " + component.PageSize + ") +1 AS PageNumber, Id ";
                pageNumberQuery += "FROM " + component.TableName + ((parentField != "") ? " Where " + parentField + " = @ParentFieldValue" : "");
                pageNumberQuery += ") A WHERE Id = @RowId";

                cmd.Parameters.Clear();

                cmd.Parameters.AddWithValue("@RowId", activeRow);
                cmd.Parameters.AddWithValue("@ParentFieldValue", parentFieldValue);
                pageNumber = Convert.ToInt32(_commonServices.ExecuteQuery_OneValue(pageNumberQuery, cmd, connectionString));
                pageNumber = pageNumber < 1 ? 1 : pageNumber;
            }

            // -----------------------------------------------
            // 5. IF QueryString empty → rebuild everything
            // -----------------------------------------------
            string queryText = view.QueryString;
            string queryTextCount = view.QueryStringCount;

            List<LoadDataFieldDto> componentFields = null;
            string tableColumns = "";
            string queryDataAccess = "";
            string querySearch = "";
            ComponentDetail componentDetail;
            List<ComponentDetail> componentDetailList = new List<ComponentDetail>();

            if (queryText == "" || queryText == null || (search != null && search.Count() > 0) || activeRow == "-1")
            {
                queryText = " Select C.Name ComponentName, C.Title ComponentTitle, C.TableName ComponentTable , C.RowsCount PageSize, C.OnCreateProc, C.OnUpdateProc, C.OnDeleteProc, C.SearchSpec, C.SortSpec, C.Type, ";
                queryText += "CF.* From Component C , ComponentField CF ";
                queryText += "Where C.Id = CF.ComponentId And C.Name = @ComponentName Order By DisplaySequence";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@ComponentName", componentName);

                var fields = new List<ComponentFieldsDto>();

                using var conn = new SqlConnection(_commonServices.getConnectionString());
                cmd = new SqlCommand(queryText, conn);

                cmd.Parameters.AddWithValue("@ComponentName", componentName);

                await conn.OpenAsync();

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var dto = new ComponentFieldsDto
                    {
                        // 🎯 Component (C.*)
                        ComponentName = reader["ComponentName"]?.ToString() ?? "",
                        ComponentTitle = _localResourceService.GetResource(reader["ComponentTitle"]?.ToString() ?? ""),
                        ComponentTable = reader["ComponentTable"] == DBNull.Value ? null : reader["ComponentTable"].ToString(),
                        PageSize = reader["PageSize"] == DBNull.Value ? 10 : Convert.ToInt32(reader["PageSize"]),
                        OnCreateProc = reader["OnCreateProc"] == DBNull.Value ? null : reader["OnCreateProc"].ToString(),
                        OnUpdateProc = reader["OnUpdateProc"] == DBNull.Value ? null : reader["OnUpdateProc"].ToString(),
                        OnDeleteProc = reader["OnDeleteProc"] == DBNull.Value ? null : reader["OnDeleteProc"].ToString(),
                        SearchSpec = reader["SearchSpec"] == DBNull.Value ? null : reader["SearchSpec"].ToString(),
                        SortSpec = reader["SortSpec"] == DBNull.Value ? null : reader["SortSpec"].ToString(),
                        Type = reader["Type"] == DBNull.Value ? null : reader["Type"].ToString(),

                        Id = Convert.ToInt32(reader["Id"]),
                        Created = reader.GetDateTime(reader.GetOrdinal("Created")),
                        CreatedBy = reader["CreatedBy"] == DBNull.Value ? null : reader["CreatedBy"].ToString(),
                        LastUpd = reader["LastUpd"] == DBNull.Value ? null : reader.GetDateTime(reader.GetOrdinal("LastUpd")),
                        LastUpdBy = reader["LastUpdBy"] == DBNull.Value ? null : reader["LastUpdBy"].ToString(),
                        GroupId = reader["GroupId"] == DBNull.Value ? null : reader.GetDecimal(reader.GetOrdinal("GroupId")),

                        Name = reader["Name"]?.ToString(),
                        TableName = reader["TableName"]?.ToString(),
                        Lable = _localResourceService.GetResource(reader["Lable"]?.ToString() ?? ""),
                        ColumnName = reader["ColumnName"]?.ToString(),
                        DataType = reader["DataType"]?.ToString(),
                        DefaultValue = reader["DefaultValue"]?.ToString(),

                        Required = Convert.ToBoolean(reader["Required"]),
                        ReadOnly = Convert.ToBoolean(reader["ReadOnly"]),
                        DisplayInList = Convert.ToBoolean(reader["DisplayInList"]),
                        DisplayInForm = Convert.ToBoolean(reader["DisplayInForm"]),

                        Comment = reader["Comment"]?.ToString(),

                        ComponentId = Convert.ToInt32(reader["ComponentId"]),
                        DisplaySequence = Convert.ToDecimal(reader["DisplaySequence"]),

                        IsCalc = Convert.ToBoolean(reader["IsCalc"]),
                        CalcExpr = reader["CalcExpr"]?.ToString(),
                        HtmlStyle = reader["HtmlStyle"]?.ToString(),
                        LookUp = reader["LookUp"] == DBNull.Value ? null : reader.GetDecimal(reader.GetOrdinal("LookUp")),
                        ImmediatePost = Convert.ToBoolean(reader["ImmediatePost"]),
                        DisplayInPopup = Convert.ToBoolean(reader["DisplayInPopup"]),
                        FileDataColumn = reader["FileDataColumn"]?.ToString(),
                        FieldSize = reader["FieldSize"] == DBNull.Value ? null : reader.GetInt32(reader.GetOrdinal("FieldSize")),
                    };

                    fields.Add(dto);
                }

                cmd.Parameters.Clear();

                if (component.TableName.ToLower() == "transactions")
                {
                    var tableName = _domainRepo.GetGroupByIdAsync(user.UserGroupId).Result?.TransactionsTable;
                    tableName = String.IsNullOrEmpty(tableName) ? "Transactions" : tableName;
                    component.TableName = tableName;
                }

                foreach (var field in fields)
                {

                    componentDetail = new ComponentDetail();
                    componentDetail.FieldName = field.Name.ToString();
                    componentDetail.TableName = field.TableName.ToString();
                    // for transactions table 
                    componentDetail.TableName = (componentDetail.TableName.ToLower() == "transactions") ? component.TableName : componentDetail.TableName;
                    componentDetail.TableColumn = field.ColumnName.ToString();
                    componentDetail.DataType = field.DataType.ToString();
                    componentDetail.ReadOnly = Convert.ToBoolean(field.ReadOnly.ToString());
                    componentDetail.Required = Convert.ToBoolean(field.Required.ToString());
                    componentDetail.DisplayInForm = Convert.ToBoolean(field.DisplayInForm.ToString());
                    componentDetail.DisplayInList = Convert.ToBoolean(field.DisplayInList.ToString());
                    componentDetail.IsCalc = Convert.ToBoolean(field.IsCalc.ToString());
                    componentDetail.CalcExpression = field.CalcExpr.ToString();
                    componentDetail.LookUp = (field.LookUp == null) ? "-1" : field.LookUp.ToString();
                    componentDetailList.Add(componentDetail);
                    string tableWithColumn = "";
                    if (componentDetail.DisplayInList)
                    {
                        tableWithColumn = (componentDetail.IsCalc && componentDetail.CalcExpression != "")
                            ? "(" + componentDetail.CalcExpression + ") "
                            : componentDetail.TableName + "." + componentDetail.TableColumn;

                        if (componentDetail.DataType == "Date")
                        {
                            tableColumns += "convert(varchar, " + tableWithColumn + ", 20) " + componentDetail.FieldName + ",";
                        }
                        else if (componentDetail.DataType == "DateTime")
                        {
                            tableColumns += "CONVERT(VARCHAR, FORMAT(" + tableWithColumn + ", 'dd/MM/yyyy hh:mm tt')) " + componentDetail.FieldName + ",";
                        }
                        else if (componentDetail.DataType == "Time")
                        {
                            tableColumns += "CONVERT(VARCHAR, FORMAT(" + tableWithColumn + ", 'hh:mm tt')) " + componentDetail.FieldName + ",";
                        }
                        else if (componentDetail.DataType == "Color")
                        {
                            tableColumns += tableWithColumn + " color_" + componentDetail.FieldName + ",";
                        }
                        else if (componentDetail.DataType == "CheckBox")
                        {
                            tableColumns += tableWithColumn + " check_" + componentDetail.FieldName + ",";
                        }
                        else if (componentDetail.DataType == "Decimal")
                        {
                            tableColumns += tableWithColumn + " decimal_" + componentDetail.FieldName + ",";
                        }
                        else if (componentDetail.DataType == "InnerButton")
                        {
                            tableColumns += tableWithColumn + " button_" + componentDetail.FieldName + ",";
                        }
                        else if (componentDetail.DataType == "File")
                        {
                            tableColumns += tableWithColumn + " file_" + componentDetail.FieldName + ",";
                        }
                        else if (componentDetail.DataType == "Button")
                        {
                            tableColumns += tableWithColumn + " button_" + componentDetail.FieldName + ",";
                        }
                        else if (componentDetail.DataType == "LookUp" && componentDetail.LookUp != "")
                        {
                            var LookUpQuery = "SELECT DBO.GetLookUpVal (" + componentDetail.LookUp + ",'" + Thread.CurrentThread.CurrentCulture.Name + "')";
                            LookUpQuery = _commonServices.ExecuteQuery_OneValue(LookUpQuery, null, _commonServices.getConnectionString());
                            tableColumns += "(" + LookUpQuery + tableWithColumn + ") " + componentDetail.FieldName + ",";
                        }
                        else
                            tableColumns += tableWithColumn + " " + componentDetail.FieldName + ",";
                    }
                    if (componentDetail.FieldName.ToString() == parentField)
                    {
                        if (componentDetail.IsCalc)
                        {
                            view.CompFieldName = componentDetail.CalcExpression;
                            parentField = componentDetail.CalcExpression;
                        }
                        else
                        {
                            view.CompFieldName = componentDetail.TableColumn;
                            parentField = componentDetail.TableName + "." + componentDetail.TableColumn;
                        }
                    }
                }

                if (viewDataAccess == 1) { } // That Mean ALL and no need to add query condition;
                else if (viewDataAccess == 2) // GROUP
                {
                    queryDataAccess = " GroupId in (SELECT ID FROM dbo.GetUserGroups ('" + user.Id + "')) ";
                }
                else // USER
                {
                    queryDataAccess = " CreatedBy =  '" + user.Id + "' ";
                }

                //if (search != null)
                //{
                //    foreach (var item in search)
                //    {
                //        string paramValue = item.Value;
                //        if (paramValue != "")
                //        {
                //            var key = item.Key;
                //            var dateFromTo = "";
                //            if (key.Contains("_From") || key.Contains("_To"))
                //            {
                //                key = key.Substring(0, key.IndexOf("_"));
                //                dateFromTo = (item.Key.Contains("_From")) ? "From" : "To";
                //            }

                //            DataRow[] dr = dataTable.Select("Name = '" + key + "'");//name[0].ToString()
                //            querySearch += (querySearch != "") ? " AND " : "";
                //            querySearch += (dr[0]["IsCalc"].ToString() == "True") ? dr[0]["CalcExpr"] : dr[0]["TableName"].ToString() + "." + dr[0]["ColumnName"].ToString();

                //            if (dateFromTo != "")
                //            {
                //                querySearch += (dateFromTo == "From") ? " >= " : " <= ";
                //                querySearch += "@" + key + "_" + dateFromTo;
                //            }
                //            else
                //            {
                //                querySearch += ((dr[0]["DataType"].ToString().ToLower() == "text") ? " like @" : " = @") + key;
                //            }

                //            if (dr[0]["DataType"].ToString().ToLower() == "checkbox")
                //            {
                //                paramValue = (paramValue == "true") ? "1" : "0";
                //            }


                //            cmd.Parameters.AddWithValue(item.Key,
                //                (dr[0]["DataType"].ToString().ToLower() == "text") ? "%" + paramValue + "%"
                //                : (dateFromTo == "To") ? paramValue + " 23:59:59"
                //                : paramValue
                //                );
                //        }
                //    }
                //}

                /// this line has been added for the case when parent field value is a calc field
                //parentField += parentFieldValue;

                queryText = "SELECT " + tableColumns.TrimEnd(',') + " FROM " + component.TableName;
                queryText += _commonServices.GetQueryWhere(parentField, querySearch, queryDataAccess, component.SearchSpec, user) + " Order By " + component.TableName + "." + component.SortSpec;
                queryText += " OFFSET @PageSize * (@PageNumber - 1) ROWS FETCH NEXT @PageSize ROWS ONLY OPTION (RECOMPILE)";

                queryText = _coreData.AddUserAttributes(queryText, user);

                // this line has stoped for the case when parent field value is a calc field
                cmd.Parameters.AddWithValue("@ParentFieldValue", parentFieldValue);
                cmd.Parameters.AddWithValue("@LangId", user.Language);

                queryTextCount = " SELECT COUNT(1) RecordCount FROM " + component.TableName + _commonServices.GetQueryWhere(parentField, querySearch, queryDataAccess, component.SearchSpec, user);

                //correct transactions table name
                while (queryText.ToLower().Contains(" transactions.") && component.TableName.ToLower() != "transactions")
                {
                    queryText = Regex.Replace(queryText, " transactions\\.", $" {component.TableName}.", RegexOptions.IgnoreCase);
                    queryTextCount = Regex.Replace(queryTextCount, " transactions\\.", $" {component.TableName}.", RegexOptions.IgnoreCase);

                }

                view.QueryString = queryText;
                view.QueryStringCount = queryTextCount;
                view.QueryStringCmd = Convert.ToBase64String(_commonServices.ConvertSqlCommandToByte(cmd));
            }

            // -----------------------------------------------
            // 6. Execute Query
            // -----------------------------------------------
            cmd.Parameters.AddWithValue("@PageSize", component.PageSize);
            cmd.Parameters.AddWithValue("@PageNumber", pageNumber);

            var listDetial =
                _commonServices.ExecuteListDictionary(queryText, cmd, connectionString);

            int recordCount = Convert.ToInt32(
                _commonServices.ExecuteQuery_OneValue(queryTextCount, cmd, connectionString)
            );

            string appletDisable =
                string.IsNullOrEmpty(parentFieldValue) || parentFieldValue == "0"
                ? "disable"
                : "enable";

            // -----------------------------------------------
            // 7. Save Updated CoreDataView (DTO)
            // -----------------------------------------------
            string updatedJson = JsonConvert.SerializeObject(coreViewList);
            string updatedBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(updatedJson));

            return new LoadDataResultDto
            {
                ListDetial = listDetial,
                RecordCount = recordCount,
                PageSize = component.PageSize,
                AppletDisable = appletDisable,
                UpdatedSession = new Dictionary<string, object>
                {
                    { "CoreDataView", updatedBase64 }
                }
            };
        }
        
        public object ILoadDetailData(Dictionary<string, string> t, ClaimsPrincipal currentUser, ISession session)
        {
            var componentName = t["componentName"].ToString();
            var activeRow = t["selectedId"].ToString();
            DataRow[] dataRow;

            if (currentUser == null || !currentUser.Identity.IsAuthenticated)
            {
                _commonServices.ThrowMessageAsException("not Autherized or Session timeout", "401");
            }

            var coreDataView = _commonServices.ByteArrayToDataTable(session.Get("CoreDataView"));

            dataRow = coreDataView.Select("ParCompName = '" + componentName + "'");
            string connectionString = _commonServices.getConnectionString();

            string pareCompFeildValue = "";
            for (int i = 0; i < dataRow.Length; i++)
            {
                var componentJson = coreDataView.Select("CompName = '" + componentName + "'")[0]["Component"].ToString();
                Component component = JsonConvert.DeserializeObject<Component>(componentJson);

                var parCompFieldName = dataRow[i]["ParCompFieldName"]?.ToString();
                var parentComponent = dataRow[i]["ParCompFieldName"]?.ToString();
                if (!string.IsNullOrEmpty(parCompFieldName) &&
                    parCompFieldName.ToLower() != "id" &&
                    parCompFieldName.ToLower() != "rowid")
                {
                    // Validate activeRow
                    if (string.IsNullOrEmpty(activeRow))
                    {
                        pareCompFeildValue = "";
                    }
                    else
                    {
                        // If Id is not numeric, quote it
                        string idValue = int.TryParse(activeRow, out _) ? activeRow : $"'{activeRow}'";

                        var getParentFieldColumnQuery = $"SELECT CASE WHEN ISCALC = 0 THEN  ColumnName ELSE CalcExpr END FROM ComponentField WHERE ComponentId = (SELECT MAX(ID) FROM Component WHERE NAME = '{componentName}') AND Name = '{parCompFieldName}'  ";
                        var parentCompColumn = _commonServices.ExecuteQuery_OneValue(getParentFieldColumnQuery, null, connectionString);
                        string subQuery = $"SELECT {parentCompColumn} FROM {component.TableName} WHERE Id = {idValue}";
                        pareCompFeildValue = _commonServices.ExecuteQuery_OneValue(subQuery, null, connectionString);
                    }
                }
                else
                {
                    pareCompFeildValue = activeRow;
                }

                dataRow[i]["ParCompFieldValue"] = pareCompFeildValue;
            }

            DataTable dt = null;

            if (dataRow != null && dataRow.Length > 0)
            {
                dt = dataRow.CopyToDataTable();

                dt.Columns.Remove("Component");
                dt.Columns.Remove("QueryString");
                dt.Columns.Remove("QueryStringCount");
                dt.Columns.Remove("QueryStringCmd");
            }
            session.Set("CoreDataView", _commonServices.DataTableToByteArray(coreDataView));
            return new Dictionary<string, object> { { "ListDetial", dt } };
        }

        public async Task<EditRowResultDto> IEditRowData(EditRowRequest t, ClaimsPrincipal currentUser)
        {
            if (currentUser == null || !currentUser.Identity.IsAuthenticated)
                _commonServices.ThrowMessageAsException("not Authorized or Session timeout", "401");

            string componentName = "CompanyUsers";
            string selectedId = "-1";

            // -----------------------------
            // 1. Load CoreDataView (DTO list)
            // -----------------------------
            var coreViewBase64 = t.CoreDataView;
            var coreJson = Encoding.UTF8.GetString(Convert.FromBase64String(coreViewBase64));
            var coreViewList = JsonConvert.DeserializeObject<List<LoadViewListItemDto>>(coreJson);

            // -----------------------------
            // 2. Load Fields cache
            // -----------------------------
            var fieldsCacheJson = t.FieldsCache ?? "[]";

            // FIX: handle [] or empty JSON safely
            Dictionary<string, List<Fields>> fieldsCache;

            if (string.IsNullOrWhiteSpace(fieldsCacheJson) ||
                fieldsCacheJson.TrimStart().StartsWith("["))
            {
                // Means it's [] → cannot be deserialized into Dictionary
                fieldsCache = new Dictionary<string, List<Fields>>();
            }
            else
            {
                fieldsCache = JsonConvert.DeserializeObject<Dictionary<string, List<Fields>>>(fieldsCacheJson)
                              ?? new Dictionary<string, List<Fields>>();
            }


            var key = componentName + selectedId;
            var username = currentUser.FindFirst("username")?.Value;
            var user = _userManager.FindByNameAsync(username).Result;

            // -----------------------------
            // 3. Validate & prepare Fields
            // -----------------------------
            List<Fields> fields;

            if (!fieldsCache.TryGetValue(key, out fields))
            {
                fields = _coreData.InitFields(coreViewList, componentName, selectedId, user);
                fieldsCache[key] = fields;
            }

            // -----------------------------
            // 4. Return + update session
            // -----------------------------
            var updatedCoreBase64 = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(coreViewList)));

            var updatedFieldsJson =
                JsonConvert.SerializeObject(fieldsCache);

            return new EditRowResultDto
            {
                ListDetial = fields,

                UpdatedSession = new Dictionary<string, object>
                {
                    { "CoreDataView", updatedCoreBase64 },
                    { "FieldsCache", updatedFieldsJson }
                }
            };
        }


        public object IDeleteRowData(Dictionary<string, string> t, ClaimsPrincipal currentUser, ISession session)
        {
            var componentName = t["componentName"].ToString();
            var selectedId = t["selectedId"].ToString();
            string queryText = "";
            string deleteQueryText = "";
            var tableName = "";
            var delProc = "";
            var connectionString = "";
            int effectedRows = 0;

            DataTable dataTable = new DataTable();
            SqlCommand cmd = new SqlCommand();

            if (currentUser == null || !currentUser.Identity.IsAuthenticated)
            {
                _commonServices.ThrowMessageAsException("not Autherized or Session timeout", "401");
            }

            var coreDataView = _commonServices.ByteArrayToDataTable(session.Get("CoreDataView"));
            DataRow[] dataRow = coreDataView.Select("CompName = '" + componentName + "'");
            Component component = JsonConvert.DeserializeObject<Component>(dataRow[0]["Component"].ToString());

            if (dataRow[0]["NoDelete"] != null && dataRow[0]["NoDelete"].ToString() == "1")
            {
                return new Dictionary<string, object> { { "ExceptionError", "لا تملك صلاحية حذف السجل" } };
            }

            tableName = component.TableName;
            delProc = component.OnDeleteProc;

            queryText = (delProc == "" || delProc.ToLower() == "null") ? "" : delProc + " @SelectedId ";
            queryText += (delProc.ToLower().Contains("usersdeleterole")) ? " ,@DeletedByUserId;" : ";";
            deleteQueryText = "Delete From " + tableName + " Where Id = @SelectedId; ";
            var user = _userManager.GetUserAsync(currentUser).Result;
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@SelectedId", selectedId);
            cmd.Parameters.AddWithValue("@DeletedByUserId", user.Id);

            if (componentName.Substring(0, 3) == "Sys")
            {
                connectionString = _commonServices.getConnectionString();
            }
            else
            {
                connectionString = _commonServices.getConnectionString();
            }

            if (queryText != "")
            {
                effectedRows = _commonServices.excuteQueryWithoutTrans(queryText, cmd, connectionString, CommandType.Text);
            }

            effectedRows = _commonServices.excuteQueryWithoutTrans(deleteQueryText, cmd, connectionString, CommandType.Text);

            return new Dictionary<string, object> { { "EffectedRows", effectedRows } };
        }

        public object IGetFile(Dictionary<string, string> t, ClaimsPrincipal currentUser, ISession session)
        {
            string componentName = t["componentName"].ToString();
            string fieldName = t["fieldName"].ToString();
            string selectedId = t["selectedId"].ToString();

            if (currentUser == null || !currentUser.Identity.IsAuthenticated)
            {
                _commonServices.ThrowMessageAsException("not Autherized or Session timeout", "401");
            }

            string queryText = "";
            SqlCommand cmd = new SqlCommand();

            cmd.Parameters.AddWithValue("@ComponentName", componentName);
            cmd.Parameters.AddWithValue("@FieldName", fieldName);
            cmd.Parameters.AddWithValue("@RowId", selectedId);

            queryText = "GetFile";
            DataTable dt = _commonServices.getDataTableFromQuery(queryText, cmd, _commonServices.getConnectionString(), CommandType.StoredProcedure);
            if (dt.Rows.Count == 1)
            {
                byte[] fieldData = (Byte[])dt.Rows[0]["FileData"];
                string fileBase64 = Convert.ToBase64String(fieldData);
                string fileName = dt.Rows[0]["FileName"].ToString();

                return new Dictionary<string, object> { { "FileName", fileName }, { "FileBase64", fileBase64 } };
            }

            return new Dictionary<string, object> { { "ExceptionError", "الملف غير موجود" } };
        }

        public DataTable IExportExcel(string componentName, ISession session)
        {
            string queryText = "";
            SqlCommand cmd = new SqlCommand();

            // Get the stored CoreDataView from session
            var coreDataView = _commonServices.ByteArrayToDataTable(session.Get("CoreDataView"));
            DataRow[] dataRow = coreDataView.Select("CompName = '" + componentName + "'");

            cmd = _commonServices.GetSqlCommanFromByte(dataRow[0]["QueryStringCmd"] as byte[]);
            queryText = dataRow[0]["QueryString"].ToString();

            // ✅ Ensure CommandText is set
            cmd.CommandText = queryText;

            cmd.Parameters.AddWithValue("@PageNumber", 1);
            cmd.Parameters.AddWithValue("@PageSize", 1000000);

            DataTable dt = _commonServices.getDataTableFromQuery(queryText, cmd, _commonServices.getConnectionString());

            // Remove specific rows for CompanyUsers
            if (componentName == "CompanyUsers")
            {
                var rowsToRemove = dt.AsEnumerable()
                                     .Where(row => row["Idno"].ToString() == "1233441000")
                                     .ToList();

                foreach (var row in rowsToRemove)
                {
                    dt.Rows.Remove(row);
                }
            }

            // Fix table headers (rename columns)
            var fields = _coreData.GetComponentFields(componentName);

            for (int i = 0; i < fields.Rows.Count; i++)
            {
                string oldColumnName = fields.Rows[i].ItemArray[11].ToString(); // current column name in dt
                string newColumnName = fields.Rows[i].ItemArray[13].ToString(); // desired display name

                // Skip "TransNo" entirely
                if (oldColumnName == "TransNo")
                    continue;

                // Ensure the column exists and the new name is not a duplicate
                if (dt.Columns.Contains(oldColumnName) && !dt.Columns.Contains(newColumnName))
                {
                    dt.Columns[oldColumnName].ColumnName = newColumnName;
                }
            }

            // 🚨 Remove the TransNo column completely if it still exists
            if (dt.Columns.Contains("TransNo"))
            {
                dt.Columns.Remove("TransNo");
            }

            return dt;
        }


        public object ISetApplicationTheme(Dictionary<string, string> t, ClaimsPrincipal currentUser)
        {
            string queryText = "";
            string applicationTheme = t["applicationTheme"].ToString(); ;

            DataTable dataTable = new DataTable();
            SqlCommand cmd = new SqlCommand();

            if (currentUser == null || !currentUser.Identity.IsAuthenticated)
            {
                _commonServices.ThrowMessageAsException("not Autherized or Session timeout", "401");
            }
            var user = _userManager.GetUserAsync(currentUser).Result;

            queryText = "UPDATE USERS SET ApplicationTheme = @ApplicationTheme Where Id = @UserId";
            cmd.Parameters.AddWithValue("@UserId", user.Id);
            cmd.Parameters.AddWithValue("@ApplicationTheme", applicationTheme);

            dataTable = _commonServices.getDataTableFromQuery(queryText, cmd, _commonServices.getConnectionString());

            return new Dictionary<string, object> { { "ListDetial", dataTable } };
        }

        public Dictionary<string, object> IGetSystemLookUp(Dictionary<string, string> t)
        {
            string lookUpName = t["lookUpName"].ToString();
            string queryText = "";
            DataTable dataTable = new DataTable();
            SqlCommand cmd = new SqlCommand();
            var currentLanguage = t.TryGetValue("langId", out var langId) ? langId : Thread.CurrentThread.CurrentCulture.Name;
            cmd.Parameters.AddWithValue("@LookUpName", lookUpName);
            cmd.Parameters.AddWithValue("@LangName", currentLanguage);
            queryText = "GetSystemLookUp";
            dataTable = _commonServices.getDataTableFromQuery(queryText, cmd, _commonServices.getConnectionString(), CommandType.StoredProcedure);

            return new Dictionary<string, object> { { "Status", "Success" }, { "DataList", dataTable } };
        }

        public string IGetSystemLookUpTransactionName(int code, string lang)
        {
            string dataTable = "";
            SqlCommand cmd = new SqlCommand();

            // Add parameter to the command
            cmd.Parameters.AddWithValue("@code", code);

            // Determine which column to select based on the language
            string columnName = lang == "ar" ? "NameAr" : "Name";

            // SQL query with dynamic column selection
            string queryText = $@"
        SELECT {columnName}
        FROM LookUpDetail
        WHERE LookUpId = 50 AND Code = @code";

            dataTable = _commonServices.ExecuteQuery_OneValue(queryText, cmd, _commonServices.getConnectionString(), CommandType.Text);

            return dataTable;
        }

        public string IGetSystemLookUpTransactionStatus(int code, string lang)
        {
            string dataTable = "";
            SqlCommand cmd = new SqlCommand();

            // Add parameter to the command
            cmd.Parameters.AddWithValue("@code", code);

            // Determine which column to select based on the language
            string columnName = lang == "ar" ? "NameAr" : "Name";

            // SQL query with dynamic column selection
            string queryText = $@"
        SELECT {columnName}
        FROM LookUpDetail
        WHERE LookUpId = 51 AND Code = @code";

            dataTable = _commonServices.ExecuteQuery_OneValue(queryText, cmd, _commonServices.getConnectionString(), CommandType.Text);

            return dataTable;
        }

        public List<SelectListItem> GetLookupList(string lookupName, string langId = "", string cascadeValue = "", bool addSelectFromList = true)
        {

            string queryText = "";
            DataTable dataTable = new DataTable();
            SqlCommand cmd = new SqlCommand();
            var currentLanguage = !String.IsNullOrEmpty(langId) ? langId : Thread.CurrentThread.CurrentCulture.Name;
            cmd.Parameters.AddWithValue("@LookUpName", lookupName);
            cmd.Parameters.AddWithValue("@LangName", currentLanguage);
            queryText = "GetSystemLookUp";
            dataTable = _commonServices.getDataTableFromQuery(queryText, cmd, _commonServices.getConnectionString(), CommandType.StoredProcedure);

            var lookupList = new List<SelectListItem>();
            var lookup = new SelectListItem();
            lookup.Text = _localResourceService.GetResource("System.Lookup.ChooseFromList");
            lookup.Value = "";
            lookupList.Add(lookup);
            foreach (DataRow row in dataTable.Rows)
            {
                lookup = new SelectListItem();
                lookup.Value = row["Code"].ToString();
                lookup.Text = row["Name"].ToString();
                lookupList.Add(lookup);
            }

            return lookupList;
        }

        public string GetLookupValue(string lookupName, string lookupCode, string langId = "", bool GetNote = false)
        {
            try
            {
                string queryText = "";
                DataTable dataTable = new DataTable();
                SqlCommand cmd = new SqlCommand();
                var currentLanguage = !String.IsNullOrEmpty(langId) ? langId : Thread.CurrentThread.CurrentCulture.Name;
                cmd.Parameters.AddWithValue("@LookUpName", lookupName);
                cmd.Parameters.AddWithValue("@Code", lookupCode);
                cmd.Parameters.AddWithValue("@LangId", currentLanguage.ToLower());
                cmd.Parameters.AddWithValue("@GetNote", GetNote ? 1 : 0);
                queryText = "select DBO.GetLookUpValue (@LookUpName, @Code, @LangId, @GetNote)";
                var val = _commonServices.ExecuteQuery_OneValue(queryText, cmd, _commonServices.getConnectionString(), CommandType.Text);
                return val;

            }
            catch (Exception err)
            {
                return _commonServices.getExceptionErrorMessage(err);
            }
        }

        public object IGetTopicPublished()
        {
            string queryText = "";
            DataTable dataTable = new DataTable();
            SqlCommand cmd = new SqlCommand();

            queryText = "SELECT TopicName FROM Topics WHERE Published = 1";
            dataTable = _commonServices.getDataTableFromQuery(queryText, cmd, _commonServices.getConnectionString());

            return new Dictionary<string, object> { { "Status", "Success" }, { "DataList", dataTable } };
        }

        public object IGetTopicDetial(Dictionary<string, string> t, string LangId = "")
        {
            string topicName = t["topicName"].ToString();
            var currentLanguage = String.IsNullOrEmpty(LangId) ? Thread.CurrentThread.CurrentCulture.Name : (LangId.ToLower().Contains("en") ? "english" : "arabic");
            string queryText = "";
            DataTable dataTable = new DataTable();
            SqlCommand cmd = new SqlCommand();

            cmd.Parameters.AddWithValue("@TopicName", topicName);
            cmd.Parameters.AddWithValue("@LangName", currentLanguage);
            queryText = "GetTopicBody";
            dataTable = _commonServices.getDataTableFromQuery(queryText, cmd, _commonServices.getConnectionString(), CommandType.StoredProcedure);

            return new Dictionary<string, object> { { "Status", "Success" }, { "DataList", dataTable } };
        }

        public IActionResult IGenerateAndSendUser([FromHeader] string userId)
        {
            var user = _userManager.FindByIdAsync(userId).Result;

            if (user == null)
            {
                return BadRequest(new { Status = "Failed", Message = "User not found" });
            }

            // Assuming you have the following properties:
            // - LastInvitationDate: DateTime? 
            // - InvitationAttempts: int?

            DateTime? lastInvitationDate = user.LastInvitationDate;
            int invitationAttempts = user.SentCounts ?? 0;
            TimeSpan requiredWaitTime;

            if (lastInvitationDate.HasValue)
            {
                TimeSpan timeSinceLastAttempt = DateTime.Now - lastInvitationDate.Value;

                if (timeSinceLastAttempt >= TimeSpan.FromMinutes(5))
                {
                    // Reset the count if more than 5 minutes have passed since the last attempt
                    user.SentCounts = 0;
                    invitationAttempts = 0;
                }

                if (invitationAttempts >= 5)
                {
                    // Prevent further invitations if the count is already 5 and it's been less than 5 minutes
                    TimeSpan waitTimeRemaining = TimeSpan.FromMinutes(5) - timeSinceLastAttempt;
                    string waitTimeMessage = string.Format(
                        _localResourceService.GetResource("System.Message.SendCredintialsCountError"),
                        Math.Ceiling(waitTimeRemaining.TotalMinutes)
                    );
                    return Ok(new { Status = "Failed", Message = waitTimeMessage });
                }
                else
                {
                    // Increment the count for this attempt
                    user.SentCounts = invitationAttempts + 1;
                }
            }
            else
            {
                // Initialize the count if no prior invitations were sent
                user.SentCounts = 1;
            }

            // Update LastInvitationDate to the current time
            user.LastInvitationDate = DateTime.Now;

            // Continue with sending the invitation...



            string password = _commonServices.GenerateRandomPassword();
            if (user.Status == 1)
            {
                return Ok(new { Status = "UserActive", Message = _localResourceService.GetResource("System.Message.UserActive") });
            }

            var code = _userManager.GeneratePasswordResetTokenAsync(user).Result;
            var resetPasswordResult = _userManager.ResetPasswordAsync(user, code, password).Result;

            if (resetPasswordResult.Succeeded)
            {
                var subject = _localResourceService.GetResource("Email.Subject.GenarateAndSendUser");
                var EmailBody = _commonServices.GetEmailTemplate("SecondTemplate");
                var SendCredentialsEmailTemplate = _commonServices.GetEmailTemplate("SendCredentialsEmailTemplate");
                var siteURL = _commonServices.IGetSettingValue("SiteURL");
                var htmlBody = string.Format(SendCredentialsEmailTemplate, user.UserName, password, siteURL);
                var EmailText = string.Format(EmailBody, user.FirstName + " " + user.LastName, "", DateTime.Now, "", "", "", "", siteURL, htmlBody, "ACCOUNT", "display:none", _localResourceService.GetResource("Message.Title.TileEmail"));

                //_emailSender.SendEmailAsync(user.Email, subject, EmailText);
                if (user.SentCounts == 0)
                {
                    _domainRepo.CreateSendNotification(new SendNotifications
                    {
                        CreatedBy = user.Id,
                        Created = DateTime.Now,
                        GroupId = user.UserGroupId,
                        LastUpdBy = user.Id,
                        LastUpd = DateTime.Now,
                        NotificationTitle = _localResourceService.GetResource("System.Message.WelcomeTitle", "en"),
                        NotificationTitleAr = _localResourceService.GetResource("System.Message.WelcomeTitle", "ar"),
                        NotificationText = _localResourceService.GetResource("System.Message.WelcomeMsg", "en"),
                        NotificationTextAr = _localResourceService.GetResource("System.Message.WelcomeMsg", "ar"),
                        SendTo = "3",
                        ReadStatus = false,
                        Status = ((int)PublicEnums.NotificationStatus.New).ToString(),
                        AssignToCompany = user.UserGroupId.ToString(),
                        AssignToUser = userId,
                        Mobile = true,
                        Web = true,
                        Email = false,
                        SMS = false,
                        IsImportant = true,

                    });

                }
                _domainRepo.CreateSendNotification(new SendNotifications
                {
                    CreatedBy = user.Id,
                    Created = DateTime.Now,
                    GroupId = user.UserGroupId,
                    LastUpdBy = user.Id,
                    LastUpd = DateTime.Now,
                    NotificationTitle = _localResourceService.GetResource("Message.Title.TileEmail"),
                    NotificationTitleAr = _localResourceService.GetResource("Message.Title.TileEmail"),
                    NotificationText = EmailText,
                    NotificationTextAr = EmailText,
                    SendTo = "3",
                    ReadStatus = false,
                    Status = ((int)PublicEnums.NotificationStatus.New).ToString(),
                    AssignToCompany = user.UserGroupId.ToString(),
                    AssignToUser = userId,
                    Mobile = false,
                    Web = false,
                    Email = true,
                    SMS = false,
                    IsImportant = true,

                });

                user.LastInvitationDate = DateTime.Now;
                user.GeneratedPassword = password;
                user.Language = Thread.CurrentThread.CurrentCulture.Name;
                user.ResetPasswordDate = DateTime.Now;
                var updateResult = _userManager.UpdateAsync(user).Result;
                if (updateResult.Succeeded)
                {
                    return Ok(new { Status = "Success", Message = _localResourceService.GetResource("Email.Message.GenerateAndSendUser") });
                }
                else
                {
                    return StatusCode(500, new { Status = "Failed", Message = "User update failed" });
                }
            }
            else
            {
                var errors = string.Join(", ", resetPasswordResult.Errors.Select(e => e.Description));
                // Log errors
                return StatusCode(500, new { Status = "Failed", Message = $"Password reset failed: {errors}" });
            }
        }

        public string IGetSettingValue(string code)
        {
            return _commonServices.IGetSettingValue(code);
        }

        public string GetFileBase64(string TableName, string FileColumn, int Id)
        {
            var queryText = $"Select CAST('' AS XML).value('xs:base64Binary(sql:column(\"{FileColumn}\"))', 'VARCHAR(MAX)')  From {TableName} where Id = {Id}";
            var fileBase64 = _commonServices.ExecuteQuery_OneValue(queryText, null, _commonServices.getConnectionString());
            return fileBase64;
        }

        public bool CheckHtmlContent(string content)
        {
            var sanitizer = new HtmlSanitizer();
            string sanitized = sanitizer.Sanitize(content);

            // Decode both to compare logical text content
            string decodedOriginal = System.Web.HttpUtility.HtmlDecode(content);
            string decodedSanitized = System.Web.HttpUtility.HtmlDecode(sanitized);

            return decodedSanitized != decodedOriginal;
        }

        #endregion
        #region Session Services

        // Create Session used in login
        public async Task<string> CreateSession(Users user, ISession session = null, string sessionKey = null, int source = 1)
        {
            string sessionId = (source == 1 && session != null) ? session.Id : Guid.NewGuid().ToString();
            var oldSession = _domainRepo.GetSignedInUsers(user.Id);
            foreach (var item in oldSession)
            {
                item.Active = false;
                _domainRepo.UpdateSignInUser(item);
            }
            if (!await _commonServices.UserSession(user, sessionId, source, sessionKey.ToString()))
            {
                return "false";
            }
            return "true";
        }

        public async Task<string> CheckSession(Users user, string? sessionKey = null)
        {
            if (user == null)
            {
                return "false";
            }
            var signedInUsers = (sessionKey != null) ? _domainRepo.GetSignedInUsers(user.Id, sessionKey)
                : _domainRepo.GetSignedInUsers(user.Id);

            if (signedInUsers.Any())
            {
                var activeSession = signedInUsers.FirstOrDefault();
                int expiredSessionDateInDays = Convert.ToInt32(IGetSettingValue("expiredSessionDateinDays"));
                int expiredSessionDateInMinutes = Convert.ToInt32(IGetSettingValue("expiredSessionDateinMinutes"));

                // Check if session is within the allowed days and also updated within the last 30 minutes
                if (activeSession.LastUpd > DateTime.Now.AddDays(-expiredSessionDateInDays) &&
                    activeSession.LastUpd >= DateTime.Now.AddMinutes(-expiredSessionDateInMinutes))
                {

                    // Refresh session timestamp
                    activeSession.LastUpd = DateTime.Now;
                    _domainRepo.UpdateSignInUser(activeSession);
                    return "true"; // Session is valid
                }
                else
                {

                    // Expired session, mark it as inactive
                    activeSession.Active = false;
                    _domainRepo.UpdateSignInUser(activeSession);
                    return "false";
                }
            }
            return "false"; // No active session found
        }

        public async Task<string> CloseSession(Users user)
        {
            var signedInUser = _domainRepo
                .GetSignedInUsers(user.Id)?
                .Where(s => s.Active)
                .OrderByDescending(s => s.Created)
                .Skip(1);

            if (signedInUser != null && signedInUser.Any())
            {
                foreach (var item in signedInUser)
                {
                    item.Active = false;
                    item.EndSession = DateTime.Now;
                    _domainRepo.UpdateSignInUser(item);
                }
                return "true";
            }
            return "false";
        }

        // Check If Session Valid used in all Core services 
        public async Task<bool> IsValidSession(ClaimsPrincipal currentUser, ISession session, string sessionKey, bool update = false, string userName = null)
        {
            try
            {
                // Get user
                var user = _userManager.GetUserAsync(currentUser).Result;
                if (user == null)
                {
                    if (session == null)
                    {
                        user = _userManager.FindByNameAsync(userName).Result;
                    }
                }
                SignedInUsers? signedInUser;
                signedInUser = _domainRepo?
                .GetSignedInUsers(user.Id)?
                .Where(u => u.SessionKey == sessionKey && u.Active && u.Source != (int)PublicEnums.SingleSessionType.Vendors)
                .OrderByDescending(u => u.Created)
                .FirstOrDefault();
                if (sessionKey == null)
                {
                    signedInUser = _domainRepo?
                        .GetSignedInUsers(user.Id)?
                        .Where(u => u.SessionId == session.Id && u.Active && u.Source != (int)PublicEnums.SingleSessionType.Vendors)
                        .OrderByDescending(u => u.Created)
                        .FirstOrDefault();
                }
                if (signedInUser != null)
                {
                    // Get session ping time from settings
                    int sessionPingTime = Convert.ToInt32(IGetSettingValue("SessionPingTime"));
                    DateTime sessionTimeThreshold = DateTime.Now.AddMinutes(-sessionPingTime);

                    if (signedInUser.LastUpd >= sessionTimeThreshold)
                    {
                        // Update session time
                        if (update)
                        {
                            signedInUser.LastUpd = DateTime.Now;
                            _domainRepo?.UpdatePingSessionTimeAsync(signedInUser);
                        }
                        return true;
                    }
                    signedInUser.Active = false;
                    _domainRepo?.UpdatePingSessionTimeAsync(signedInUser);
                }
                // If session is invalid, sign out
                if (session != null) session.Clear();
                await _signInManager.SignOutAsync();
                return false;
            }
            catch (Exception ex)
            {
                return false;
            }

        }
        public Dictionary<string, string> GetUserSessionKeys(Users? user)
        {
            var result = new Dictionary<string, string>();

            var signedInUser = _domainRepo.GetSignedInUsers(user.Id)?.FirstOrDefault();

            if (signedInUser != null)
            {
                var newIP = signedInUser?.IPAddress ?? string.Empty;
                var country = signedInUser?.Country ?? string.Empty;
                var region = signedInUser?.Region ?? string.Empty;
                var location = signedInUser?.Location ?? string.Empty;
                var deviceModel = signedInUser?.DeviceModel ?? string.Empty;
                string source = (signedInUser?.Source == 1)
                    ? _localResourceService.GetResource("System.Message.WebApplication")
                    : _localResourceService.GetResource("System.Message.MobileApplication");

                // Fill dictionary
                result["Status"] = "Invalid Session";
                result["IPAddress"] = newIP;
                result["Country"] = country;
                result["Region"] = region;
                result["Location"] = location;
                result["Source"] = source;
                result["DeviceModel"] = deviceModel;
            }

            return result;
        }

        #endregion
        private async Task<Dictionary<string, Component>> LoadAllComponentDefinitions(List<string> names)
        {
            if (names == null || names.Count == 0)
                return new Dictionary<string, Component>();

            var result = new Dictionary<string, Component>();

            string sql = $@"
        SELECT 
            Name,
            Title,
            TableName,
            RowsCount AS PageSize,
            OnCreateProc,
            OnUpdateProc,
            OnDeleteProc,
            SearchSpec,
            SortSpec,
            Type
        FROM Component
        WHERE Name IN ({string.Join(",", names.Select((n, i) => $"@n{i}"))})
    ";

            using (var conn = new SqlConnection(_commonServices.getConnectionString()))
            using (var cmd = new SqlCommand(sql, conn))
            {
                for (int i = 0; i < names.Count; i++)
                    cmd.Parameters.AddWithValue($"@n{i}", names[i]);

                await conn.OpenAsync();
                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var def = new Component
                    {
                        Name = reader["Name"].ToString(),
                        Title = reader["Title"].ToString(),
                        TableName = reader["TableName"].ToString(),
                        PageSize = Convert.ToInt32(reader["PageSize"]),
                        OnCreateProc = reader["OnCreateProc"].ToString(),
                        OnUpdateProc = reader["OnUpdateProc"].ToString(),
                        OnDeleteProc = reader["OnDeleteProc"].ToString(),
                        SearchSpec = reader["SearchSpec"].ToString(),
                        SortSpec = (reader["SortSpec"].ToString() == "" ? "Id DESC" : reader["SortSpec"].ToString()),
                        Type = Convert.ToInt32(reader["Type"])
                    };

                    result[def.Name] = def;
                }
            }

            return result;
        }

    }
}
