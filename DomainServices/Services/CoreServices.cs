using DomainServices.Data;
using DomainServices.Data.Repository;
using DomainServices.Models;
using DomainServices.Services.Interfaces;
using Ganss.Xss;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using System.Data;
using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;
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

        public Dictionary<string, object> ILoadViewList(ClaimsPrincipal currentUser, ISession session)
        {
            string queryText = "";
            string userFullName = "";
            string applicationTheme = "";
            DataTable dataTable = new DataTable();
            SqlCommand cmd = new SqlCommand();

            if (currentUser == null || !currentUser.Identity.IsAuthenticated)
            {
                _commonServices.ThrowMessageAsException("not Autherized or Session timeout", "401");
            }
            var user = _userManager.GetUserAsync(currentUser).Result;
            userFullName = user.FirstName + " " + user.LastName;
            applicationTheme = user.ApplicationTheme;

            queryText = " SELECT DISTINCT V.Id, V.Type, V.ViewStyle, V.Name, V.Title, V.ViewSequence, V.MainCategory, V.ViewIcon, PD.ReadOnly ";
            queryText += " FROM RolePermissions RP, Permissions p, PermissionDetails pd, Views V ";
            queryText += " Where RP.PermissionId = P.Id And P.Id = Pd.ParentId And PD.ViewId = V.Id And RP.RoleId = @RoleId ORDER BY ViewSequence ";
            cmd.Parameters.AddWithValue("@RoleId", user.RoleId);
            dataTable = _commonServices.getDataTableFromQuery(queryText, cmd, _commonServices.getConnectionString());

            // set localization resource name
            foreach (DataRow row in dataTable.Rows)
            {
                row["Title"] = _localResourceService.GetResource(row["Title"].ToString());
            }

            return new Dictionary<string, object> { { "ListDetial", dataTable }, { "UserFullName", userFullName }, { "ApplicationTheme", applicationTheme } };
        }

        public Dictionary<string, object> ILoadView(Dictionary<string, string> t, ClaimsPrincipal currentUser, ISession session)
        {
            string queryText = "";
            string viewName = t["viewName"].ToString();
            DataTable dataTable = new DataTable();
            DataTable viewDataTable = new DataTable();
            SqlCommand cmd = new SqlCommand();

            if (currentUser == null || !currentUser.Identity.IsAuthenticated)
            {
                _commonServices.ThrowMessageAsException("not Autherized or Session timeout", "401");
            }

            var user = _userManager.GetUserAsync(currentUser).Result;
            queryText = " Select DISTINCT Seq, V.ClientURL, V.Title ViewTitle , VC.ViewId, V.ViewStyle, VC.ViewDataAccess, C.Id CompId, C.Name CompName,C.Title CompTitle, VC.CompFieldId, VC.ParCompId, VC.ParCompFieldId, PD.ReadOnly, C.ExportExcel, VC.NoInsert, VC.NoUpdate, VC.NoDelete, "
                + " (Select CF.Name FROM ComponentField CF WHERE VC.CompFieldId = CF.Id) CompFieldName, "
                + " (Select PC.Name FROM Component PC WHERE VC.ParCompId = PC.Id) ParCompName, "
                + " (Select PCF.Name FROM ComponentField PCF WHERE  VC.ParCompFieldId = PCF.Id) ParCompFieldName, "
                + " ISNULL((SELECT TOP 1 1 FROM ViewComponent VCC WHERE VC.CompId = VCC.ParCompId AND ViewId = V.Id), 0) HasDetail "
                + " From Views V, ViewComponent VC, Component C , RolePermissions RP, Permissions p, PermissionDetails pd "
                + " Where V.Id = VC.ViewId And VC.CompId = C.Id And RP.PermissionId = P.Id And P.Id = Pd.ParentId And PD.ViewId = V.Id "
                + " And V.Name = @ViewName AND RP.RoleId = @RoleId Order by Seq; ";
            cmd.Parameters.AddWithValue("@ViewName", viewName);
            cmd.Parameters.AddWithValue("@RoleId", user.RoleId);

            dataTable = _commonServices.getDataTableFromQuery(queryText, cmd, _commonServices.getConnectionString());
            var currentLang = System.Threading.Thread.CurrentThread.CurrentCulture.Name.ToLower();
            var descColumnName = currentLang.Contains("ar") ? "DescriptionAr" : "DescriptionEn";
            queryText = $"Select Top 1 {descColumnName} Description From Views where Name = @ViewName";
            var description = _commonServices.ExecuteQuery_OneValue(queryText, cmd, _commonServices.getConnectionString());
            // set localization resource name
            foreach (DataRow row in dataTable.Rows)
            {
                row["ViewTitle"] = _localResourceService.GetResource(row["ViewTitle"].ToString());
                row["CompTitle"] = _localResourceService.GetResource(row["CompTitle"].ToString());
            }

            _coreData.sharedVariable.View = dataTable;
            _coreData.sharedVariable.View.Columns.Add("ParCompFieldValue", typeof(string));
            _coreData.sharedVariable.View.Columns.Add("Fields", typeof(string));
            _coreData.sharedVariable.View.Columns.Add("Component", typeof(string));
            _coreData.sharedVariable.View.Columns.Add("QueryString", typeof(string));
            _coreData.sharedVariable.View.Columns.Add("QueryStringCount", typeof(string));
            _coreData.sharedVariable.View.Columns.Add("QueryStringCmd", typeof(byte[]));


            _coreData.InitComponents();

            // clear previous Component and Fields from Session
            foreach (var sessionKey in session.Keys)
            {
                if (sessionKey.Contains("Fields"))
                {
                    session.Remove(sessionKey);
                }
            }

            //set session
            session.Set("CoreDataView", _commonServices.DataTableToByteArray(_coreData.sharedVariable.View));

            return new Dictionary<string, object> { { "ListDetial", dataTable }, { "ViewDescription", description } };
        }

        public object IGetTemplate(Dictionary<string, string> t, ClaimsPrincipal currentUser)
        {
            var componentName = t["componentName"].ToString();
            string queryText = "";
            DataTable dataTable;
            SqlCommand cmd = new SqlCommand();

            if (currentUser == null || !currentUser.Identity.IsAuthenticated)
            {
                _commonServices.ThrowMessageAsException("not Autherized or Session timeout", "401");
            }

            cmd.Parameters.Clear();
            queryText = " Select Component.Name ComponentName, Component.Title ComponentTitle, Component.RowsCount PageSize, ";
            queryText += "ComponentField.* From [dbo].[Component] , [dbo].[ComponentField]";
            queryText += "Where Component.Id = ComponentField.ComponentId And Component.Name = @ComponentName Order By DisplaySequence";
            cmd.Parameters.AddWithValue("@ComponentName", componentName);

            dataTable = _commonServices.getDataTableFromQuery(queryText, cmd, _commonServices.getConnectionString());

            // set localization resource name
            foreach (DataRow row in dataTable.Rows)
            {
                row["ComponentTitle"] = _localResourceService.GetResource(row["ComponentTitle"].ToString());
                row["Lable"] = _localResourceService.GetResource(row["Lable"].ToString());

            }

            return new Dictionary<string, object> { { "ListDetial", dataTable } };
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

            dataRow = coreDataView.Select("CompName = '" + componentName + "'");
            parentField = dataRow[0]["CompFieldName"].ToString();
            parentFieldValue = dataRow[0]["ParCompFieldValue"].ToString();
            var sessionFields = session.GetString("Fields" + componentName + selectedId);
            var fields = (sessionFields == null || t["selectedId"] == "-1") ?
                _coreData.InitFields(coreDataView, componentName, selectedId, user) :
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

        public object ILoadData(Dictionary<string, string> t, ClaimsPrincipal currentUser, ISession session)
        {
            var componentName = t["componentName"].ToString();
            session.SetString("ComponentName", componentName);
            int pageNumber = t["pageNumber"] == "-1" ? -1 : Convert.ToInt32(t["pageNumber"]);
            var activeRow = String.IsNullOrEmpty(t["selectedId"]) ? "-1" : t["selectedId"];
            var search = JsonConvert.DeserializeObject<Dictionary<string, string>>(t["search"]);
            int recordCount = 0;
            var viewDataAccess = 1;
            var queryDataAccess = "";
            var queryText = "";
            var querySearch = "";
            var queryTextCount = "";
            var tableColumns = "";
            var appletDisable = "enable";
            var parentField = "-1";
            object parentFieldValue = "-1";
            string connectionString = _commonServices.getConnectionString();

            DataTable dataTable = new DataTable();
            DataRow[] dataRow;
            SqlCommand cmd;
            Component component = new Component();
            ComponentDetail componentDetail;
            List<ComponentDetail> componentDetailList = new List<ComponentDetail>();

            if (currentUser == null || !currentUser.Identity.IsAuthenticated)
            {
                _commonServices.ThrowMessageAsException("not Autherized or Session timeout", "401");
            }

            var user = _userManager.GetUserAsync(currentUser).Result;

            var coreDataView = _commonServices.ByteArrayToDataTable(session.Get("CoreDataView"));
            dataRow = coreDataView.Select("CompName = '" + componentName + "'");
            viewDataAccess = Convert.ToInt32(dataRow[0]["ViewDataAccess"]);

            queryText = dataRow[0]["QueryString"].ToString();
            queryTextCount = dataRow[0]["QueryStringCount"].ToString();
            cmd = _commonServices.GetSqlCommanFromByte(dataRow[0]["QueryStringCmd"] as byte[]);

            if (cmd == null)
            {
                cmd = new SqlCommand();
            }

            component = JsonConvert.DeserializeObject<Component>(dataRow[0]["Component"].ToString());
            parentField = dataRow[0]["CompFieldName"].ToString();
            parentFieldValue = dataRow[0]["ParCompFieldValue"].ToString();

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

            if (queryText == "" || queryText == null || (search != null && search.Count() > 0) || activeRow == "-1")
            {
                queryText = " Select C.Name ComponentName, C.Title ComponentTitle, C.TableName ComponentTable , C.RowsCount PageSize, C.OnCreateProc, C.OnUpdateProc, C.OnDeleteProc, C.SearchSpec, C.SortSpec, C.Type, ";
                queryText += "CF.* From Component C , ComponentField CF ";
                queryText += "Where C.Id = CF.ComponentId And C.Name = @ComponentName Order By DisplaySequence";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@ComponentName", componentName);

                dataTable = _commonServices.getDataTableFromQuery(queryText, cmd, _commonServices.getConnectionString());

                cmd.Parameters.Clear();

                if (component.TableName.ToLower() == "transactions")
                {
                    var tableName = _domainRepo.GetGroupByIdAsync(user.UserGroupId).Result?.TransactionsTable;
                    tableName = String.IsNullOrEmpty(tableName) ? "Transactions" : tableName;
                    component.TableName = tableName;
                }

                foreach (DataRow row in dataTable.Rows)
                {

                    componentDetail = new ComponentDetail();
                    componentDetail.FieldName = row["Name"].ToString();
                    componentDetail.TableName = row["TableName"].ToString();
                    // for transactions table 
                    componentDetail.TableName = (componentDetail.TableName.ToLower() == "transactions") ? component.TableName : componentDetail.TableName;
                    componentDetail.TableColumn = row["ColumnName"].ToString();
                    componentDetail.DataType = row["DataType"].ToString();
                    componentDetail.ReadOnly = Convert.ToBoolean(row["ReadOnly"].ToString());
                    componentDetail.Required = Convert.ToBoolean(row["Required"].ToString());
                    componentDetail.DisplayInForm = Convert.ToBoolean(row["DisplayInForm"].ToString());
                    componentDetail.DisplayInList = Convert.ToBoolean(row["DisplayInList"].ToString());
                    componentDetail.IsCalc = Convert.ToBoolean(row["IsCalc"].ToString());
                    componentDetail.CalcExpression = row["CalcExpr"].ToString();
                    componentDetail.LookUp = (row["LookUp"] == null) ? "-1" : row["LookUp"].ToString();
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
                            dataRow[0]["CompFieldName"] = componentDetail.CalcExpression;
                            parentField = componentDetail.CalcExpression;
                        }
                        else
                        {
                            dataRow[0]["CompFieldName"] = componentDetail.TableColumn;
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

                if (search != null)
                {
                    foreach (var item in search)
                    {
                        string paramValue = item.Value;
                        if (paramValue != "")
                        {
                            var key = item.Key;
                            var dateFromTo = "";
                            if (key.Contains("_From") || key.Contains("_To"))
                            {
                                key = key.Substring(0, key.IndexOf("_"));
                                dateFromTo = (item.Key.Contains("_From")) ? "From" : "To";
                            }

                            DataRow[] dr = dataTable.Select("Name = '" + key + "'");//name[0].ToString()
                            querySearch += (querySearch != "") ? " AND " : "";
                            querySearch += (dr[0]["IsCalc"].ToString() == "True") ? dr[0]["CalcExpr"] : dr[0]["TableName"].ToString() + "." + dr[0]["ColumnName"].ToString();

                            if (dateFromTo != "")
                            {
                                querySearch += (dateFromTo == "From") ? " >= " : " <= ";
                                querySearch += "@" + key + "_" + dateFromTo;
                            }
                            else
                            {
                                querySearch += ((dr[0]["DataType"].ToString().ToLower() == "text") ? " like @" : " = @") + key;
                            }

                            if (dr[0]["DataType"].ToString().ToLower() == "checkbox")
                            {
                                paramValue = (paramValue == "true") ? "1" : "0";
                            }


                            cmd.Parameters.AddWithValue(item.Key,
                                (dr[0]["DataType"].ToString().ToLower() == "text") ? "%" + paramValue + "%"
                                : (dateFromTo == "To") ? paramValue + " 23:59:59"
                                : paramValue
                                );
                        }
                    }
                }

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

                dataRow[0]["QueryString"] = queryText;
                dataRow[0]["QueryStringCount"] = queryTextCount;
                dataRow[0]["QueryStringCmd"] = _commonServices.ConvertSqlCommandToByte(cmd);
            }

            cmd.Parameters.AddWithValue("@PageSize", component.PageSize);
            cmd.Parameters.AddWithValue("@PageNumber", pageNumber);

            dataTable = _commonServices.getDataTableFromQuery(queryText, cmd, connectionString, CommandType.Text);

            cmd.Parameters.RemoveAt("@PageSize");
            cmd.Parameters.RemoveAt("@PageNumber");

            recordCount = Convert.ToInt32(_commonServices.ExecuteQuery_OneValue(queryTextCount, cmd, connectionString));


            if (parentField != "" && (parentFieldValue.ToString() == "0" || String.IsNullOrEmpty(parentFieldValue.ToString())))
            {
                appletDisable = "disable";
            }
            session.Set("CoreDataView", _commonServices.DataTableToByteArray(coreDataView));

            return new Dictionary<string, object> {
                { "ListDetial", dataTable },
                { "RecordCount", recordCount },
                { "PageSize", component.PageSize },
                { "AppletDisable", appletDisable } };
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

        public object IEditRowData(Dictionary<string, string> t, ClaimsPrincipal currentUser, ISession session)
        {
            var componentName = t["componentName"].ToString();
            var activeRow = t["selectedId"].ToString();
            DataRow[] dataRow;

            if (currentUser == null || !currentUser.Identity.IsAuthenticated)
            {
                _commonServices.ThrowMessageAsException("not Autherized or Session timeout", "401");
            }

            var coreDataView = _commonServices.ByteArrayToDataTable(session.Get("CoreDataView"));
            var sessionFields = session.GetString("Fields" + componentName + activeRow);
            Users user = _userManager.GetUserAsync(currentUser).Result;

            var fields = (sessionFields == null || t["selectedId"] == "-1") ?
                _coreData.InitFields(coreDataView, componentName, activeRow, user) :
                JsonConvert.DeserializeObject<List<Fields>>(sessionFields);

            var sessionKey = "Fields" + componentName + activeRow;
            session.SetString(sessionKey, JsonConvert.SerializeObject(fields));

            return new Dictionary<string, object> { { "ListDetial", fields } };
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
    }
}
