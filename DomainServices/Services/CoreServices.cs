using DomainServices.Data;
using DomainServices.Data.Repository;
using DomainServices.Models;
using DomainServices.Models.Core;
using DomainServices.Services.Interfaces;
using EservicesHub.Models.Core;
using Ganss.Xss;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Newtonsoft.Json;
using Quartz.Util;
using System.Data;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
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
        private readonly ILogger<CoreServices> _logger;

        public CoreServices(
            UserManager<Users> userManager,
            CommonServices commonServices,
            ILocalResourceService localResourceService,
            CoreData coreData,
            DomainDBContext.DomainRepo domainRepo,
            ILogger<CoreServices> logger)
        {
            _userManager = userManager;
            _commonServices = commonServices;
            _localResourceService = localResourceService;
            _coreData = coreData;
            _domainRepo = domainRepo;
            _logger = logger;
        }
        #region Methods

        public async Task<GetUserViewsResponse> IGetUserViewsAsync(ClaimsPrincipal currentUser)
        {
            try
            {
                if (currentUser == null || !currentUser.Identity.IsAuthenticated)
                    _commonServices.ThrowMessageAsException("not Autherized or Session timeout", "401");

                var username = currentUser.FindFirst("preferred_username")?.Value
                               ?? throw new Exception("Invalid user context.");

                var user = await _userManager.FindByNameAsync(username)
                           ?? throw new Exception("User not found.");

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

                using var conn = new SqlConnection(_commonServices.getConnectionString());
                using var cmd = new SqlCommand(query, conn);
                cmd.Parameters.Add("@RoleId", SqlDbType.Int).Value = user.RoleId;

                await conn.OpenAsync();
                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    list.Add(new ViewListItemDto
                    {
                        Id = reader.GetFieldValue<decimal>(reader.GetOrdinal("Id")),
                        Type = reader.GetFieldValue<decimal>(reader.GetOrdinal("Type")),
                        ViewStyle = reader.IsDBNull(reader.GetOrdinal("ViewStyle"))
                            ? null
                            : reader.GetFieldValue<int>(reader.GetOrdinal("ViewStyle")),
                        Name = reader.GetFieldValue<string>(reader.GetOrdinal("Name")),
                        Title = _localResourceService.GetResource(reader.GetFieldValue<string>(reader.GetOrdinal("Title")), user.Language),
                        ViewSequence = reader.GetFieldValue<decimal>(reader.GetOrdinal("ViewSequence")),
                        MainCategory = reader.IsDBNull(reader.GetOrdinal("MainCategory"))
                            ? null
                            : reader.GetFieldValue<decimal>(reader.GetOrdinal("MainCategory")),
                        ViewIcon = reader.GetFieldValue<string>(reader.GetOrdinal("ViewIcon")),
                        ReadOnly = reader.GetFieldValue<bool>(reader.GetOrdinal("ReadOnly"))
                    });
                }

                _logger.LogInformation("User views fetched successfully.");
                return new GetUserViewsResponse
                {
                    ListDetial = list,
                    UserFullName = $"{user.FirstName} {user.LastName}",
                    ApplicationTheme = user.ApplicationTheme
                };
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Error in IGetUserViewsAsync: {Message}", ex.Message);
                throw new Exception(ex.Message + " / " + ex.InnerException);
            }
        }

        public async Task<LoadViewResponse> ILoadView(Dictionary<string, string> t, ClaimsPrincipal currentUser)
        {
            // -------------------------------
            // 1. Guard clauses
            // -------------------------------
            if (currentUser?.Identity?.IsAuthenticated != true)
                _commonServices.ThrowMessageAsException("not Autherized or Session timeout", "401");

            if (!t.TryGetValue("viewName", out var viewName) || string.IsNullOrWhiteSpace(viewName))
                throw new Exception("viewName is required");

            var username = currentUser.FindFirst("preferred_username")?.Value;
            var user = await _userManager.FindByNameAsync(username);

            var lang = user.Language;

            // -------------------------------
            // 2. SQL → Load View + Component Metadata
            // -------------------------------
            const string sql = @"
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

            // -------------------------------
            // 3. Execute query
            // -------------------------------
            var list = new List<LoadViewListItemDto>();
            string viewDescription = "";

            using (var conn = new SqlConnection(_commonServices.getConnectionString()))
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@RoleId", user.RoleId);
                cmd.Parameters.AddWithValue("@ViewName", viewName);
                cmd.Parameters.AddWithValue("@Lang", lang);

                await conn.OpenAsync();
                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var dto = new LoadViewListItemDto
                    {
                        Seq = reader.GetFieldValue<decimal>(0),
                        ClientURL = reader.IsDBNull(1) ? "" : reader.GetString(1),
                        ViewTitle = reader.IsDBNull(2) ? "" : _localResourceService.GetResource(reader.GetString(2), user.Language) ?? "",
                        ViewId = reader.GetFieldValue<decimal>(3),
                        ViewStyle = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                        ViewDataAccess = reader.GetInt32(5),
                        CompId = reader.GetFieldValue<decimal>(6),
                        CompName = reader.IsDBNull(7) ? "" : reader.GetString(7),
                        CompTitle = reader.IsDBNull(8) ? "" : _localResourceService.GetResource(reader.GetString(8), user.Language) ?? "",
                        CompFieldId = reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                        ParCompId = reader.IsDBNull(10) ? null : reader.GetDecimal(10),
                        ParCompFieldId = reader.IsDBNull(11) ? null : reader.GetDecimal(11),
                        ReadOnly = !reader.IsDBNull(12) && reader.GetBoolean(12),
                        ExportExcel = !reader.IsDBNull(13) && reader.GetBoolean(13),
                        NoInsert = !reader.IsDBNull(14) && reader.GetBoolean(14),
                        NoUpdate = !reader.IsDBNull(15) && reader.GetBoolean(15),
                        NoDelete = !reader.IsDBNull(16) && reader.GetBoolean(16),
                        CompFieldName = reader.IsDBNull(17) ? "" : reader.GetString(17),
                        ParCompName = reader.IsDBNull(18) ? "" : reader.GetString(18),
                        ParCompFieldName = reader.IsDBNull(19) ? "" : reader.GetString(19),
                        HasDetail = reader.GetInt32(20),
                        ViewDescription = reader.IsDBNull(21) ? "" : reader.GetString(21)
                    };

                    viewDescription = dto.ViewDescription;
                    list.Add(dto);
                }
            }

            // -------------------------------
            // 4. Load component definitions
            // -------------------------------
            var componentNames = list.Select(x => x.CompName).Distinct().ToList();
            var componentMap = await LoadAllComponentDefinitions(componentNames);

            foreach (var item in list)
            {
                if (componentMap.TryGetValue(item.CompName, out var compDef))
                    item.Component = compDef;
            }

            // -------------------------------
            // 5. Build CoreDataView using DataShape
            // -------------------------------
            DataShape coreDataShape = CoreDataShapeMapper.ToShape(list);
            string coreDataViewJson = JsonConvert.SerializeObject(coreDataShape);


            return new LoadViewResponse
            {
                ListDetial = list,
                ViewDescription = viewDescription,
                UpdatedSession = new Dictionary<string, object>
                {
                    { "CoreDataView", coreDataViewJson }
                }
            };
        }

        public async Task<ListDetialResponse> IGetTemplate(Dictionary<string, string> t, ClaimsPrincipal currentUser)
        {
            // ---------------------------------------
            // 1. Authorization & Validation
            // ---------------------------------------
            if (currentUser?.Identity?.IsAuthenticated != true)
                _commonServices.ThrowMessageAsException("not Authorized or Session timeout", "401");

            if (!t.TryGetValue("componentName", out var componentName) ||
                string.IsNullOrWhiteSpace(componentName))
                throw new Exception("componentName is required");

            var username = currentUser.FindFirst("preferred_username")?.Value;
            var user = await _userManager.FindByNameAsync(username);
            // ---------------------------------------
            // 2. SQL
            // ---------------------------------------
            const string sql = @"
                SELECT 
                    C.Name AS ComponentName, 
                    C.Title AS ComponentTitle, 
                    C.RowsCount AS PageSize, 
                    CF.*
                FROM Component C
                JOIN ComponentField CF ON C.Id = CF.ComponentId
                WHERE C.Name = @ComponentName
                ORDER BY CF.DisplaySequence;
            ";

            // ---------------------------------------
            // 3. Execute
            // ---------------------------------------
            var list = new List<ListDetialDto>();
            using var conn = new SqlConnection(_commonServices.getConnectionString());
            using var cmd = new SqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("@ComponentName", componentName);

            await conn.OpenAsync();
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var dto = new ListDetialDto
                {
                    ComponentName = reader["ComponentName"]?.ToString() ?? "",
                    ComponentTitle = _localResourceService.GetResource(reader["ComponentTitle"]?.ToString() ?? "", user.Language) ?? "",
                    PageSize = reader["PageSize"] == DBNull.Value ? 10 : Convert.ToInt32(reader["PageSize"]),

                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                    Created = reader.GetDateTime(reader.GetOrdinal("Created")),
                    CreatedBy = reader["CreatedBy"]?.ToString(),

                    LastUpd = reader["LastUpd"] == DBNull.Value
                        ? null
                        : reader.GetDateTime(reader.GetOrdinal("LastUpd")),

                    LastUpdBy = reader["LastUpdBy"]?.ToString(),

                    GroupId = reader["GroupId"] == DBNull.Value
                        ? null
                        : Convert.ToDecimal(reader["GroupId"]),

                    Name = reader["Name"]?.ToString(),
                    TableName = reader["TableName"]?.ToString(),
                    Lable = _localResourceService.GetResource((reader["Lable"]?.ToString() ?? ""), user.Language) ?? "",
                    ColumnName = reader["ColumnName"]?.ToString(),
                    DataType = reader["DataType"]?.ToString(),
                    DefaultValue = reader["DefaultValue"]?.ToString(),

                    Required = reader.GetBoolean(reader.GetOrdinal("Required")),
                    ReadOnly = reader.GetBoolean(reader.GetOrdinal("ReadOnly")),
                    DisplayInList = reader.GetBoolean(reader.GetOrdinal("DisplayInList")),
                    DisplayInForm = reader.GetBoolean(reader.GetOrdinal("DisplayInForm")),
                    Comment = reader["Comment"]?.ToString(),

                    ComponentId = reader.GetInt32(reader.GetOrdinal("ComponentId")),
                    DisplaySequence = Convert.ToDecimal(reader["DisplaySequence"]),

                    IsCalc = reader.GetBoolean(reader.GetOrdinal("IsCalc")),
                    CalcExpr = reader["CalcExpr"]?.ToString(),
                    HtmlStyle = reader["HtmlStyle"]?.ToString(),

                    LookUp = reader["LookUp"] == DBNull.Value
                        ? null
                        : Convert.ToDecimal(reader["LookUp"]),

                    ImmediatePost = reader.GetBoolean(reader.GetOrdinal("ImmediatePost")),
                    DisplayInPopup = reader.GetBoolean(reader.GetOrdinal("DisplayInPopup")),
                    FileDataColumn = reader["FileDataColumn"]?.ToString(),

                    FieldSize = reader["FieldSize"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(reader["FieldSize"])
                };

                list.Add(dto);
            }

            // ---------------------------------------
            // 4. Return DTO
            // ---------------------------------------
            return new ListDetialResponse
            {
                ListDetial = list
            };
        }

        public async Task<SubmitDataResultDto> ISubmitData(SubmitDataRequest t, ClaimsPrincipal currentUser)
        {
            // ----------------------------------------------------------
            // 1) AUTH
            // ----------------------------------------------------------
            if (currentUser?.Identity?.IsAuthenticated != true)
                _commonServices.ThrowMessageAsException("not Authorized or Session timeout", "401");

            var username = currentUser.FindFirst("preferred_username")?.Value;
            var user = await _userManager.FindByNameAsync(username);

            var componentName = t.ComponentName ?? "";
            var saveType = t.SaveType?.ToLower() ?? "new"; // new | edit
            var selectedId = string.IsNullOrWhiteSpace(t.SelectedId) ? "-1" : t.SelectedId;

            var submittedData = t.DataList ?? new Dictionary<string, object>();
            var connectionString = _commonServices.getConnectionString();
            var cmd = new SqlCommand();

            // ----------------------------------------------------------
            // 2) LOAD VIEW / COMPONENT
            // ----------------------------------------------------------
            var shape = JsonConvert.DeserializeObject<DataShape>(t.CoreDataView ?? "[]") ?? new DataShape();
            var views = CoreDataShapeMapper.FromShape(shape);

            var view = views.FirstOrDefault(v => v.CompName == componentName)
                ?? throw new Exception($"Component '{componentName}' not found in CoreDataView.");

            var component = view.Component
                ?? throw new Exception($"Component '{componentName}' has no Component definition.");

            var parentField = view.CompFieldName ?? "1";
            var parentFieldValue = view.ParCompFieldValue ?? "";

            // ----------------------------------------------------------
            // 3) PERMISSIONS
            // ----------------------------------------------------------
            if (saveType == "new" && view.NoInsert)
                return _coreData.FailArabic("لا تملك صلاحية الاضافة", shape);

            if (saveType == "edit" && view.NoUpdate)
                return _coreData.FailArabic("لا تملك صلاحية التعديل", shape);

            // ----------------------------------------------------------
            // 4) LOAD FIELDS (from cache or fresh)
            // ----------------------------------------------------------
            List<Fields> fields;

            if (string.IsNullOrEmpty(t.FieldsCache) || selectedId == "-1")
            {
                fields = await _coreData.InitFieldsStrong(component, selectedId, user);
            }
            else
            {
                // FIX: FieldsCache is a dictionary, not a list
                var dict = JsonConvert.DeserializeObject<Dictionary<string, List<Fields>>>(t.FieldsCache)
                           ?? new Dictionary<string, List<Fields>>();

                // take the first list inside the dictionary
                fields = dict.Where(x => x.Key == componentName).FirstOrDefault().Value ?? new List<Fields>();
            }


            // ----------------------------------------------------------
            // 5) PROCESS FIELDS (validation + SQL building)
            // ----------------------------------------------------------
            var insertCols = new StringBuilder();
            var insertVals = new StringBuilder();
            var updateSet = new StringBuilder();
            bool hasGroupId = false;

            foreach (var row in fields)
            {
                bool shouldProcess =
                    ((!row.ReadOnly && row.Visible)
                    || (!string.IsNullOrWhiteSpace(row.DefaultValue.ToString()) && saveType == "new"))
                    && row.Type?.ToLower() != "button"
                    && !row.IsCalc;

                if (!shouldProcess)
                    continue;

                // -----------------------------
                // Pull and normalize submitted value
                // -----------------------------
                submittedData.TryGetValue(_commonServices.GetValue(row.Name), out var raw);
                raw = _coreData.NormalizeValue(raw);

                if (raw != null)
                    row.Value = raw;

                // -----------------------------
                // Required validation
                // -----------------------------
                if (row.Required &&
                    (row.Value == null || string.IsNullOrWhiteSpace(row.Value.ToString())))
                    throw new Exception($"column {row.Name} is Required", new Exception(row.Name));

                // -----------------------------
                // Apply default value if needed
                // -----------------------------
                if ((row.Value == null || string.IsNullOrWhiteSpace(row.Value.ToString()))
                    && !string.IsNullOrWhiteSpace(row.DefaultValue.ToString()))
                {
                    row.Value = _commonServices.GetFieldDefaultValue(fields, row.DefaultValue);
                }

                // -----------------------------
                // HTML content validation
                // -----------------------------
                if (row.Value != null && CheckHtmlContent(row.Value.ToString()))
                {
                    if (user.RoleId != 1 && componentName != "SysView")
                        throw new Exception("Contains Html Content");
                }

                // -----------------------------
                // Type-specific conversions
                // -----------------------------
                row.Value = _coreData.ConvertFieldValue(row.Type, row.Value);

                // ----------------------------------------------------
                // SQL BUILDING – INSERT
                // ----------------------------------------------------
                if (saveType == "new")
                {
                    if (row.ColumnName?.Equals("groupid", StringComparison.OrdinalIgnoreCase) == true)
                        hasGroupId = true;

                    if (row.Type?.ToLower() != "file")
                    {
                        insertCols.Append(row.ColumnName + ",");
                        insertVals.Append("@" + row.Name + ",");
                    }
                    else
                    {
                        _coreData.HandleFileInsert(row, insertCols, insertVals);
                    }
                }
                else // ----------------------------------------------------
                     // SQL BUILDING – UPDATE
                     // ----------------------------------------------------
                {
                    _coreData.HandleFileUpdate(row, updateSet);
                }

                // ----------------------------------------------------
                // ADD SQL PARAMETER
                // ----------------------------------------------------
                _coreData.AddSqlParameter(cmd, row);
            }

            // ----------------------------------------------------------
            // 6) PARENT FIELD → MAP NAME TO COLUMN
            // ----------------------------------------------------------
            foreach (var r in fields)
            {
                if (r.Name.Equals(parentField, StringComparison.OrdinalIgnoreCase))
                    parentField = r.ColumnName ?? parentField;
            }

            // ----------------------------------------------------------
            // 7) COMPLETE SQL INSERT/UPDATE
            // ----------------------------------------------------------
            string rowId;
            var sql = _coreData.BuildFinalSql(saveType, component.TableName, cmd, insertCols, insertVals, updateSet, parentField, parentFieldValue, hasGroupId, user, selectedId, out rowId);

            // ----------------------------------------------------------
            // 8) EXECUTE SQL
            // ----------------------------------------------------------
            try
            {
                _coreData.ExecuteSubmitSql(sql, cmd, component, user, saveType, ref rowId);
            }
            catch (SqlException ex)
            {
                if (ex.Number == 2627 || ex.Number == 2601)
                {
                    // 2627 = Violation of PRIMARY KEY or UNIQUE constraint
                    // 2601 = Cannot insert duplicate value in index
                    return new SubmitDataResultDto
                    {
                        ExceptionError = "A record with the same values already exists.",
                        SaveType = saveType,
                        RowId = selectedId,
                        UpdatedSession = new Dictionary<string, object>()
                    };
                }

                throw; // rethrow all other errors
            }

            // ----------------------------------------------------------
            // 9) UPDATE FIELDS CACHE (equivalent to old session.SetString)
            // ----------------------------------------------------------
            string cacheKey = $"{componentName}-{rowId}";

            // Build a fresh dictionary (same as EditRowData)
            var fieldsCache = new Dictionary<string, List<Fields>>
            {
                { cacheKey, fields }
            };

            var updatedSession = new Dictionary<string, object>
            {
                { "FieldsCache", JsonConvert.SerializeObject(fieldsCache) }
            };

            // ----------------------------------------------------------
            // 10) RETURN
            // ----------------------------------------------------------
            return new SubmitDataResultDto
            {
                RowId = rowId,
                SaveType = saveType,
                UpdatedSession = updatedSession
            };
        }


        public async Task<LoadDataResultDto> ILoadData(LoadDataRequest t, ClaimsPrincipal currentUser)
        {
            // ------------------------------------------------------------
            // 1. Authentication Validation
            // ------------------------------------------------------------
            if (currentUser?.Identity?.IsAuthenticated != true)
                _commonServices.ThrowMessageAsException("not Authorized or Session timeout", "401");

            // Extract request parameters
            var componentName = t.ComponentName ?? string.Empty;
            var activeRow = string.IsNullOrEmpty(t.SelectedId) ? "-1" : t.SelectedId;
            var pageNumber = t.PageNumber == -1 ? -1 : t.PageNumber;
            var search = t.Search;

            var connectionString = _commonServices.getConnectionString();

            // ------------------------------------------------------------
            // 2. Resolve Current User
            // ------------------------------------------------------------
            var username = currentUser.FindFirst("preferred_username")?.Value;
            var user = await _userManager.FindByNameAsync(username);

            // ------------------------------------------------------------
            // 3. Decode CoreDataView (DataShape → DTO List)
            // ------------------------------------------------------------
            var shapeJson = t.CoreDataView ?? "[]";
            var shape = JsonConvert.DeserializeObject<DataShape>(shapeJson) ?? new DataShape();

            var coreViewList = CoreDataShapeMapper.FromShape(shape);
            var view = coreViewList.FirstOrDefault(v => v.CompName == componentName);

            if (view == null) throw new Exception($"View for component '{componentName}' not found in CoreDataView.");

            var component = view.Component ?? throw new Exception($"Component for '{componentName}' is not initialized in CoreDataView.");

            var parentField = view.CompFieldName ?? string.Empty;
            var parentFieldValue = view.ParCompFieldValue ?? string.Empty;

            // ------------------------------------------------------------
            // 4. Page Number Resolution (matches legacy behavior)
            // ------------------------------------------------------------
            var cmd = new SqlCommand();

            if (pageNumber == -1)
            {
                var pageNumberQuery =
                    "SELECT PageNumber FROM (" +
                    $"  SELECT ((ROW_NUMBER() OVER (ORDER BY {component.SortSpec})) / {component.PageSize}) + 1 AS PageNumber, Id " +
                    $"  FROM {component.TableName} " +
                    (string.IsNullOrEmpty(parentField) ? "" : $" WHERE {parentField} = @ParentFieldValue") +
                    ") A WHERE Id = @RowId";

                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@RowId", activeRow);
                cmd.Parameters.AddWithValue("@ParentFieldValue", parentFieldValue);

                var result = _commonServices.ExecuteQuery_OneValue(pageNumberQuery, cmd, connectionString);
                pageNumber = string.IsNullOrEmpty(result) ? 1 : Convert.ToInt32(result);
                pageNumber = pageNumber < 1 ? 1 : pageNumber;
            }

            // ------------------------------------------------------------
            // 5. Resolve or Rebuild Query Definitions
            // ------------------------------------------------------------
            var queryText = view.QueryString;
            var queryCount = view.QueryStringCount;

            var hasSearch = search != null && search.Count > 0;
            var rebuildQuery = string.IsNullOrEmpty(queryText) || hasSearch || activeRow == "-1";

            if (rebuildQuery)
            {
                // Build complete SELECT + COUNT queries based on ComponentField metadata
                _coreData.BuildLoadDataQuery(component: component, view: view, search: search, hasSearch : hasSearch, user: user, cmd: cmd, query: out queryText, queryCount: out queryCount);

                // Cache the base command snapshot before paging parameters are added
                var cmdBytes = _commonServices.ConvertSqlCommandToByte(cmd);
                view.QueryString = queryText;
                view.QueryStringCount = queryCount;
                view.QueryStringCmd = Convert.ToBase64String(cmdBytes);
            }
            else
            {
                // Restore cached SqlCommand (metadata parameters only)
                if (!string.IsNullOrWhiteSpace(view.QueryStringCmd))
                {
                    try { cmd = _commonServices.GetSqlCommanFromByte(Convert.FromBase64String(view.QueryStringCmd)) ?? new SqlCommand(); }
                    catch { cmd = new SqlCommand(); }
                }

                queryText = view.QueryString;
                queryCount = view.QueryStringCount;
            }

            // ------------------------------------------------------------
            // 6. Apply Paging Parameters (added per request, never cached)
            // ------------------------------------------------------------
            if (!cmd.Parameters.Contains("@PageSize"))
                cmd.Parameters.AddWithValue("@PageSize", component.PageSize);
            else
                cmd.Parameters["@PageSize"].Value = component.PageSize;

            if (!cmd.Parameters.Contains("@PageNumber"))
                cmd.Parameters.AddWithValue("@PageNumber", pageNumber);
            else
                cmd.Parameters["@PageNumber"].Value = pageNumber;

            // Parent filter parameter (used only when parentField is part of WHERE)
            if (!string.IsNullOrEmpty(parentField))
            {
                if (!cmd.Parameters.Contains("@ParentFieldValue"))
                    cmd.Parameters.AddWithValue("@ParentFieldValue", parentFieldValue);
                else
                    cmd.Parameters["@ParentFieldValue"].Value = parentFieldValue;
            }

            // Language parameter (required for lookup evaluations)
            cmd.Parameters.AddWithValue("@LangId", user.Language);

            // ------------------------------------------------------------
            // 7. Execute Main Query + Count Query
            // ------------------------------------------------------------
            var listDetial = _commonServices.ExecuteListDictionary(queryText, cmd, connectionString);

            var recordCountStr = _commonServices.ExecuteQuery_OneValue(queryCount, cmd, connectionString);

            var recordCount = string.IsNullOrEmpty(recordCountStr) ? 0 : Convert.ToInt32(recordCountStr);

            // ------------------------------------------------------------
            // 8. Applet State Resolution (matches legacy logic)
            // ------------------------------------------------------------
            var appletDisable = !string.IsNullOrEmpty(parentField) && (string.IsNullOrEmpty(parentFieldValue) || parentFieldValue == "0") ? "disable" : "enable";

            // ------------------------------------------------------------
            // 9. Update CoreDataView (DataShape)
            // ------------------------------------------------------------
            var updatedShape = CoreDataShapeMapper.ToShape(coreViewList);
            var updatedCoreJson = JsonConvert.SerializeObject(updatedShape);

            // ------------------------------------------------------------
            // 10. Build Final Response
            // ------------------------------------------------------------
            return new LoadDataResultDto
            {
                ListDetial = listDetial,
                RecordCount = recordCount,
                PageSize = component.PageSize,
                AppletDisable = appletDisable,
                UpdatedSession = new Dictionary<string, object>
                {
                    { "CoreDataView", updatedCoreJson }
                }
            };
        }

        public async Task<LoadDetailDataResponse> ILoadDetailData(LoadDetailDataRequest t, ClaimsPrincipal currentUser)
        {
            // ---------------------------------------------------------
            // 1. Authentication
            // ---------------------------------------------------------
            if (currentUser?.Identity?.IsAuthenticated != true)
                _commonServices.ThrowMessageAsException("not authorized or session timeout", "401");

            string componentName = t.ComponentName ?? "";
            string activeRow = string.IsNullOrEmpty(t.SelectedId) ? "-1" : t.SelectedId;

            // ---------------------------------------------------------
            // 2. Decode CoreDataView (DataShape)
            // ---------------------------------------------------------
            var shape = JsonConvert.DeserializeObject<DataShape>(t.CoreDataView ?? "[]")
                ?? new DataShape();

            var coreViewList = CoreDataShapeMapper.FromShape(shape);

            // Parent view (main grid)
            var parentView = coreViewList.FirstOrDefault(x => x.CompName == componentName);

            if (parentView == null)
                throw new Exception($"View for component '{componentName}' not found in CoreDataView.");

            // ---------------------------------------------------------
            // 3. Identify all detail components
            // ---------------------------------------------------------
            var detailItems = coreViewList
                .Where(x => x.ParCompName == componentName)
                .ToList();

            if (detailItems.Count == 0)
            {
                return new LoadDetailDataResponse
                {
                    ListDetial = new List<LoadDetailItemDto>(),
                    UpdatedSession = new Dictionary<string, object>
                    {
                        { "CoreDataView", t.CoreDataView }
                    }
                };
            }

            string connectionString = _commonServices.getConnectionString();

            // ---------------------------------------------------------
            // 4. Resolve parent field value for each detail item
            // ---------------------------------------------------------
            foreach (var item in detailItems)
            {
                var component = item.Component;
                string parField = item.ParCompFieldName;

                string parentValue = activeRow; // default behavior

                if (!string.IsNullOrEmpty(parField) &&
                    parField.ToLower() != "id" &&
                    parField.ToLower() != "rowid")
                {
                    // --- Step 1: find actual DB column (ColumnName or CalcExpr) ---
                    string resolveColumnSql =
                        $"SELECT CASE WHEN IsCalc = 0 THEN ColumnName ELSE CalcExpr END " +
                        $"FROM ComponentField " +
                        $"WHERE ComponentId = (SELECT MAX(Id) FROM Component WHERE Name = @CompName) " +
                        $"AND Name = @FieldName";

                    var cmd = new SqlCommand();
                    cmd.Parameters.AddWithValue("@CompName", componentName);
                    cmd.Parameters.AddWithValue("@FieldName", parField);

                    var dbColumn = _commonServices.ExecuteQuery_OneValue(resolveColumnSql, cmd, connectionString);

                    // --- Step 2: extract parent value from parent component table ---
                    if (!string.IsNullOrWhiteSpace(dbColumn))
                    {
                        bool numeric = int.TryParse(activeRow, out _);
                        string idValue = numeric ? activeRow : $"'{activeRow}'";

                        string valueQuery = $"SELECT {dbColumn} FROM {item.ParCompName} WHERE Id = {idValue}";
                        parentValue = _commonServices.ExecuteQuery_OneValue(valueQuery, null, connectionString);
                    }
                }

                item.ParCompFieldValue = parentValue; // save value in DataShape
            }

            // ---------------------------------------------------------
            // 5. Produce response DTO
            // ---------------------------------------------------------
            var resultList = detailItems.Select(x => new LoadDetailItemDto
            {
                Seq = x.Seq,
                ClientURL = x.ClientURL,
                ViewTitle = x.ViewTitle,
                ViewId = x.ViewId,
                CompId = x.CompId,
                CompName = x.CompName,
                CompTitle = x.CompTitle,
                CompFieldId = x.CompFieldId,
                ParCompId = x.ParCompId,
                ParCompName = x.ParCompName,
                ParCompFieldId = x.ParCompFieldId,
                ParCompFieldName = x.ParCompFieldName,
                ParCompFieldValue = x.ParCompFieldValue,
                ReadOnly = x.ReadOnly,
                Component = x.Component
            }).ToList();

            // ---------------------------------------------------------
            // 6. Update DataShape
            // ---------------------------------------------------------
            var updatedShape = CoreDataShapeMapper.ToShape(coreViewList);
            string updatedJson = JsonConvert.SerializeObject(updatedShape);

            return new LoadDetailDataResponse
            {
                ListDetial = resultList,
                UpdatedSession = new Dictionary<string, object>
                {
                    { "CoreDataView", updatedJson }
                }
            };
        }

        public async Task<EditRowResultDto> IEditRowData(EditRowRequest t, ClaimsPrincipal currentUser)
        {
            if (currentUser?.Identity?.IsAuthenticated != true)
                _commonServices.ThrowMessageAsException("not Authorized or Session timeout", "401");

            string componentName = t.ComponentName;
            string selectedId = string.IsNullOrWhiteSpace(t.SelectedId) ? "-1" : t.SelectedId;

            // ---------------------------------------------------------
            // 1. Decode CoreDataView
            // ---------------------------------------------------------
            var shape = JsonConvert.DeserializeObject<DataShape>(t.CoreDataView ?? "{}")
                        ?? new DataShape();

            var coreViewList = CoreDataShapeMapper.FromShape(shape);

            // ---------------------------------------------------------
            // 2. Load FieldsCache (Session)
            // ---------------------------------------------------------
            Dictionary<string, List<Fields>> fieldsCache =
                string.IsNullOrWhiteSpace(t.FieldsCache)
                    ? new Dictionary<string, List<Fields>>()
                    : JsonConvert.DeserializeObject<Dictionary<string, List<Fields>>>(t.FieldsCache)
                      ?? new Dictionary<string, List<Fields>>();

            string cacheKey = componentName;

            // ---------------------------------------------------------
            // 3. Get user + component
            // ---------------------------------------------------------
            var username = currentUser.FindFirst("preferred_username")?.Value;
            var user = await _userManager.FindByNameAsync(username);

            var viewItem = coreViewList.FirstOrDefault(x => x.CompName == componentName);
            if (viewItem == null)
                throw new Exception($"Component {componentName} not found.");

            var component = viewItem.Component;

            List<Fields> fields;

            // ---------------------------------------------------------
            // 4. EXACT LEGACY LOGIC:
            //    If cache exists → use it
            //    If not → build fresh
            // ---------------------------------------------------------
            if (!fieldsCache.ContainsKey(cacheKey) || selectedId != "-1")
            {
                // Build fresh fields
                fields = await _coreData.InitFieldsStrong(component, selectedId, user);
                fieldsCache[cacheKey] = fields;
                
            }
            else
            {
                // Reuse — identical to old session behavior
                fields = fieldsCache[cacheKey];
            }

            // ---------------------------------------------------------
            // 5. Return with updated session
            // ---------------------------------------------------------
            return new EditRowResultDto
            {
                ListDetial = fields,
                UpdatedSession = new Dictionary<string, object>
                {
                    { "FieldsCache", JsonConvert.SerializeObject(fieldsCache) }
                }
            };
        }

        public async Task<DeleteRowResultDto> IDeleteRowData(DeleteRowRequest t, ClaimsPrincipal currentUser)
        {
            // -------------------------------------------------
            // 1. Validate
            // -------------------------------------------------
            if (currentUser?.Identity?.IsAuthenticated != true)
                _commonServices.ThrowMessageAsException("not Authorized or Session timeout", "401");

            string componentName = t.ComponentName ?? "";
            string selectedId = t.SelectedId ?? "-1";

            var username = currentUser.FindFirst("preferred_username")?.Value;
            var user = await _userManager.FindByNameAsync(username);

            // -------------------------------------------------
            // 2. Load component from CoreDataView (DataShape)
            // -------------------------------------------------
            var shape = JsonConvert.DeserializeObject<DataShape>(t.CoreDataView ?? "[]")
                        ?? new DataShape();

            var viewList = CoreDataShapeMapper.FromShape(shape);
            var view = viewList.FirstOrDefault(v => v.CompName == componentName);

            if (view == null)
                throw new Exception($"Component '{componentName}' not found.");

            if (view.NoDelete)
            {
                return new DeleteRowResultDto
                {
                    ExceptionError = "لا تملك صلاحية حذف السجل"
                };
            }

            var component = view.Component;
            string tableName = component.TableName;
            string delProc = component.OnDeleteProc ?? "";

            // -------------------------------------------------
            // 3. Prepare SQL
            // -------------------------------------------------
            SqlCommand cmd = new SqlCommand();
            cmd.Parameters.AddWithValue("@SelectedId", selectedId);
            cmd.Parameters.AddWithValue("@DeletedByUserId", user.Id);

            string deleteSql = $"DELETE FROM {tableName} WHERE Id = @SelectedId;";

            string procSql = "";
            if (!string.IsNullOrWhiteSpace(delProc) && delProc.ToLower() != "null")
            {
                procSql = $"{delProc} @SelectedId";
                if (delProc.ToLower().Contains("usersdeleterole"))
                    procSql += " ,@DeletedByUserId;";
                else
                    procSql += ";";
            }

            string connectionString = _commonServices.getConnectionString();

            // -------------------------------------------------
            // 4. Execute (wrapped safe)
            // -------------------------------------------------
            int affected;

            try
            {
                // Execute delete stored procedure (if exists)
                if (!string.IsNullOrWhiteSpace(procSql))
                    _commonServices.ExecuteNonQuerySafe(procSql, cmd, connectionString);

                // Execute physical delete
                affected = _commonServices.ExecuteNonQuerySafe(deleteSql, cmd, connectionString);
            }
            catch (SqlException ex)
            {
                // Handle FK constraint violation
                if (ex.Number == 547)
                {
                    return new DeleteRowResultDto
                    {
                        ExceptionError = "لا يمكن حذف السجل لوجود بيانات مرتبطة."
                    };
                }

                // Handle other SQL issues
                return new DeleteRowResultDto
                {
                    ExceptionError = ex.Message
                };
            }

            // -------------------------------------------------
            // 5. Return success
            // -------------------------------------------------
            return new DeleteRowResultDto
            {
                EffectedRows = affected
            };
        }

        public async Task<IGetFileResultDto> IGetFile(IGetFileRequest t)
        {
            string componentName = t.ComponentName ?? "";
            string fieldName = t.FieldName ?? "";
            int rowId = int.TryParse(t.SelectedId, out var parsed) ? parsed : 0;

            using var cmd = new SqlCommand("dbo.GetFile");

            cmd.Parameters.Add("@ComponentName", SqlDbType.NVarChar, 50).Value = componentName;
            cmd.Parameters.Add("@FieldName", SqlDbType.NVarChar, 50).Value = fieldName;
            cmd.Parameters.Add("@RowId", SqlDbType.Int).Value = rowId;

            string connStr = _commonServices.getConnectionString();

            // IMPORTANT: This must call stored procedures correctly
            DataShape shape = _commonServices.ExecuteQuery_DataShape("", cmd, connStr, CommandType.StoredProcedure);

            if (shape.Rows.Count == 0)
            {
                return new IGetFileResultDto { ExceptionError = "الملف غير موجود" };
            }

            var row = shape.Rows[0];

            if (!row.ContainsKey("FileData") || row["FileData"] == null)
            {
                return new IGetFileResultDto { ExceptionError = "الملف غير موجود" };
            }

            byte[] data = (byte[])row["FileData"];
            string base64 = Convert.ToBase64String(data);
            string fileName = row.ContainsKey("FileName") ? row["FileName"]?.ToString() ?? "" : "";

            return new IGetFileResultDto
            {
                FileName = fileName,
                FileBase64 = base64
            };
        }


        public async Task<DataShape> IExportExcel(ExportExcelRequest data, ClaimsPrincipal currentUser)
        {
            if (currentUser?.Identity?.IsAuthenticated != true)
                _commonServices.ThrowMessageAsException("not Authorized or Session timeout", "401");

            var username = currentUser.FindFirst("preferred_username")?.Value;
            var user = await _userManager.FindByNameAsync(username);

            //------------------------------------------------------------
            // 1) Decode CoreDataView
            //------------------------------------------------------------
            var shape = JsonConvert.DeserializeObject<DataShape>(data.CoreDataView ?? "{}")
                        ?? new DataShape();

            var viewList = CoreDataShapeMapper.FromShape(shape);

            var viewItem = viewList.FirstOrDefault(x => x.CompName == data.ComponentName);
            if (viewItem == null)
                return new DataShape();

            var component = viewItem.Component;

            //------------------------------------------------------------
            // 2) Build Excel SELECT (FULL FIELDS)
            //------------------------------------------------------------
            SqlCommand cmd = new SqlCommand();

            _coreData.BuildExportExcelQuery(
                component,
                viewItem,
                user,
                cmd,
                out string exportQuery
            );

            cmd.CommandText = exportQuery;
            cmd.Parameters.AddWithValue("@LangId", user.Language);

            //------------------------------------------------------------
            // 3) Load Fields for localized headers
            //------------------------------------------------------------
            var fields = await _coreData.GetComponentFields(data.ComponentName);

            // Build mapping: FieldName → LocalizedLabel
            var headerMap = fields
                .GroupBy(f => f.Name)
                .ToDictionary(
                    g => g.Key,
                    g => string.IsNullOrWhiteSpace(g.First().Lable)
                            ? g.First().Name
                            : g.First().Lable
                );

            //------------------------------------------------------------
            // 4) Execute SQL → DataShape
            //------------------------------------------------------------
            DataShape ds = new DataShape();

            using (var conn = new SqlConnection(_commonServices.getConnectionString()))
            {
                cmd.Connection = conn;
                await conn.OpenAsync();

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    if (!reader.HasRows)
                        return ds;

                    //----------------------------------------------------
                    // Columns
                    //----------------------------------------------------
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        string originalName = reader.GetName(i);

                        string displayName = headerMap.ContainsKey(originalName)
                            ? headerMap[originalName]
                            : originalName;

                        ds.Columns.Add(new ColumnShape
                        {
                            Name = displayName,
                            DataType = reader.GetFieldType(i).Name,
                            ReadOnly = false,
                            Required = false
                        });
                    }

                    //----------------------------------------------------
                    // Rows
                    //----------------------------------------------------
                    while (await reader.ReadAsync())
                    {
                        var row = new Dictionary<string, object>();

                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            string originalName = reader.GetName(i);

                            string displayName = headerMap.ContainsKey(originalName)
                                ? headerMap[originalName]
                                : originalName;

                            object val = reader.IsDBNull(i) ? "" : reader.GetValue(i);

                            row[displayName] = val;
                        }

                        ds.Rows.Add(row);
                    }
                }
            }

            return ds;
        }

        public async Task<ApplicationThemeResponse> ISetApplicationTheme(ApplicationThemeRequest t, ClaimsPrincipal currentUser)
        {
            if (currentUser?.Identity?.IsAuthenticated != true)
                _commonServices.ThrowMessageAsException("not Authorized or Session timeout", "401");

            var user = await _userManager.GetUserAsync(currentUser);

            string theme = t.ApplicationTheme;

            string sql = "UPDATE USERS SET ApplicationTheme = @ApplicationTheme WHERE Id = @UserId";
            var cmd = new SqlCommand(sql);

            cmd.Parameters.AddWithValue("@UserId", user.Id);
            cmd.Parameters.AddWithValue("@ApplicationTheme", theme);

            var listDetial = _commonServices.ExecuteListDictionary(sql, cmd, _commonServices.getConnectionString());

            return new ApplicationThemeResponse
            {
                ListDetial = listDetial
            };
        }

        public async Task<Dictionary<string, object>> IGetSystemLookUp(Dictionary<string, string> t)
        {
            string lookUpName = t["lookUpName"];

            SqlCommand cmd = new SqlCommand();
            var currentLanguage = t.TryGetValue("langId", out var langId)
                ? langId
                : Thread.CurrentThread.CurrentCulture.Name;

            cmd.Parameters.AddWithValue("@LookUpName", lookUpName);
            cmd.Parameters.AddWithValue("@LangName", currentLanguage);

            string queryText = "GetSystemLookUp";

            DataShape lookupList = _commonServices.ExecuteQuery_DataShape(
                queryText,
                cmd,
                _commonServices.getConnectionString(),
                CommandType.StoredProcedure
            );

            return new Dictionary<string, object>
            {
                { "Status", "Success" },
                { "DataList", lookupList }
            };
        }

        public async Task<TopicsPublishedResponse> IGetTopicPublished()
        {
            string query = "SELECT TopicName FROM Topics WHERE Published = 1";

            var dataList = _commonServices.ExecuteListDictionary(query, new SqlCommand(), _commonServices.getConnectionString());

            return new TopicsPublishedResponse
            {
                Status = "Success",
                DataList = dataList
            };
        }

        public async Task<object> IGetTopicDetial(TopicDetailRequest t, string LangId = "")
        {
            string topicName = t.TopicName;

            var currentLanguage = string.IsNullOrEmpty(LangId)
                ? Thread.CurrentThread.CurrentCulture.Name
                : (LangId.ToLower().Contains("en") ? "english" : "arabic");

            SqlCommand cmd = new SqlCommand();
            cmd.Parameters.AddWithValue("@TopicName", topicName);
            cmd.Parameters.AddWithValue("@LangName", currentLanguage);

            string queryText = "GetTopicBody";

            // Execute stored procedure and return DataShape
            DataShape dataShape = _commonServices.ExecuteQuery_DataShape(
                queryText,
                cmd,
                _commonServices.getConnectionString()
            );

            return new
            {
                Status = "Success",
                DataList = dataShape
            };
        }

        public async Task<IActionResult> IGenerateAndSendUser(GenerateAndSendUserRequest data)
        {
            var user = await _userManager.FindByIdAsync(data.UserId);

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
                        _localResourceService.GetResource("System.Message.SendCredintialsCountError", user.Language),
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
                return Ok(new { Status = "UserActive", Message = _localResourceService.GetResource("System.Message.UserActive", user.Language) });
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
                var EmailText = string.Format(EmailBody, user.FirstName + " " + user.LastName, "", DateTime.Now, "", "", "", "", siteURL, htmlBody, "ACCOUNT", "display:none", _localResourceService.GetResource("Message.Title.TileEmail", user.Language));

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
                        AssignToUser = user.Id,
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
                    AssignToUser = user.Id,
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
                    return Ok(new { Status = "Success", Message = _localResourceService.GetResource("Email.Message.GenerateAndSendUser", user.Language) });
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
