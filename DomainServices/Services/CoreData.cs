using DomainServices.Data;
using DomainServices.Models;
using DomainServices.Models.Core;
using DomainServices.Services.Interfaces;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using System.ComponentModel;
using System.Data;
using System.Threading.Tasks;
using Component = DomainServices.Models.Component;

namespace DomainServices.Services
{
    public class CoreData
    {
        #region Fields

        private readonly CommonServices _commonServices;
        public SharedVariable sharedVariable;

        #endregion

        #region Constructor

        public CoreData(CommonServices commonServices)
        {
            _commonServices = commonServices;
            sharedVariable = new SharedVariable();
        }

        #endregion

        #region Methods

        public void InitComponents()
        {
            DataTable dataTable = new DataTable();
            SqlCommand cmd = new SqlCommand();

            foreach (DataRow dr in sharedVariable.View.Rows)
            {
                string queryText = " Select C.Name ComponentName, C.Title ComponentTitle, C.TableName ComponentTable , C.RowsCount PageSize, C.OnCreateProc, C.OnUpdateProc, C.OnDeleteProc, C.SearchSpec, C.SortSpec, C.Type ";
                cmd.Parameters.Clear();

                queryText += " From Component C  Where C.Name = @ComponentName";
                cmd.Parameters.AddWithValue("@ComponentName", dr["CompName"]);

                dataTable = _commonServices.getDataTableFromQuery(queryText, cmd, _commonServices.getConnectionString());

                Component component = dr["Component"] != null ? JsonConvert.DeserializeObject<Component>(dr["Component"].ToString()) : null;

                if (component == null)
                {
                    component = new Component();
                    component.TableName = dataTable.Rows[0]["ComponentTable"].ToString();
                    component.OnCreateProc = dataTable.Rows[0]["OnCreateProc"].ToString();
                    component.OnUpdateProc = dataTable.Rows[0]["OnUpdateProc"].ToString();
                    component.OnDeleteProc = dataTable.Rows[0]["OnDeleteProc"].ToString();
                    component.SearchSpec = dataTable.Rows[0]["SearchSpec"].ToString();
                    component.SortSpec = (dataTable.Rows[0]["SortSpec"].ToString() == "") ? "Id DESC" : dataTable.Rows[0]["SortSpec"].ToString();
                    component.PageSize = Convert.ToInt32(dataTable.Rows[0]["PageSize"].ToString());
                    component.Type = Convert.ToInt32(dataTable.Rows[0]["Type"].ToString());
                }

                dr["Component"] = JsonConvert.SerializeObject(component);
            }
        }

        public async Task<List<Fields>> InitFields(List<LoadViewListItemDto> coreDataView, string componentName, string activeRow, Users user)
        {
            var queryText = "";
            var tableColumns = "";
            string connectionString = _commonServices.getConnectionString();
            var currentLanguage = Thread.CurrentThread.CurrentCulture.Name;

            // define objects
            SqlCommand cmd = new SqlCommand();
            var dataRow = coreDataView.First(x => x.CompName == componentName);
            var componentJson = dataRow.Component?.ToString() ?? "";
            Console.WriteLine("componentJson = " + componentJson);
            var component = dataRow.Component;

            var Fields = new List<Fields>();
            var dataTable = await GetComponentFields(componentName);

            foreach (var row in dataTable)
            {
                var field = new Fields
                {
                    Name = row.Name.ToString(),
                    ColumnName = row.ColumnName.ToString(),
                    //Value = row["DefaultValue"].ToString(),
                    DefaultValue = row.DefaultValue.ToString(),
                    Visible = Convert.ToBoolean(row.DisplayInForm.ToString()),
                    ReadOnly = Convert.ToBoolean(row.ReadOnly.ToString()),
                    Required = Convert.ToBoolean(row.Required.ToString()),
                    Type = row.DataType.ToString(),
                    LookUpQuery = row.LookUp.ToString(),
                    LookUpValues = null,
                    ImmediatePost = Convert.ToBoolean(row.ImmediatePost.ToString()),
                    DisplayInPopup = Convert.ToBoolean(row.DisplayInPopup.ToString()),
                    IsCalc = Convert.ToBoolean(row.IsCalc.ToString()),
                    CalcExpr = row.CalcExpr.ToString(),
                    FileDataColumn = row.FileDataColumn.ToString(),
                    FileDataValue = "",
                    FieldSize = (row.FieldSize == null) ? 12 : Convert.ToInt32(row.FieldSize)
                };

                if (field.LookUpQuery != "")
                {
                    queryText = "SELECT Id, Name, Type, Component, TableName, ParentColumn, ChildColumn, FieldValue, FieldCode, SearchSpec From LookUps Where Id = @LookUpId";
                    cmd.Parameters.Clear();
                    cmd.Parameters.AddWithValue("@LookUpId", field.LookUpQuery);
                    DataTable LookUpsTable = _commonServices.getDataTableFromQuery(queryText, cmd, _commonServices.getConnectionString());
                    LookUpsTable.TableName = "LookUpsTable";
                    var LookUpType = LookUpsTable.Rows[0]["Type"].ToString();

                    if (LookUpsTable.Rows.Count > 0)
                    {
                        if (LookUpType == "1")
                        {
                            field.Type = "lookup-1";
                            var columnNameByLang = (currentLanguage.ToLower().Contains("en")) ? "Name" : "NameAr";
                            field.LookUpQuery = $"Select TOP (500) {columnNameByLang} Name, Code From LookUpDetail Where LookUpId = @LookUpId";

                            DataTable lookupDt = _commonServices.getDataTableFromQuery(field.LookUpQuery, cmd, _commonServices.getConnectionString());
                            field.LookUpValues = _commonServices.GetLookupListFormDataTable(lookupDt);
                        }
                        else if (LookUpType == "2")
                        {
                            field.Type = "lookup-1";
                            queryText = "SELECT C.Id , C.Name, C.TableName, CASE WHEN IsCalc = 1 THEN C.CalcExpr ELSE C.ColumnName END ColumnName, C.CalcExpr, L.FieldCode, L.FieldValue, L.SearchSpec  FROM ComponentField C , LookUps L WHERE L.Component = C.ComponentId AND L.Id = @LookUpId";
                            DataTable lookUpDT = (_commonServices.getDataTableFromQuery(queryText, cmd, _commonServices.getConnectionString()) as DataTable);

                            connectionString = _commonServices.getConnectionString();

                            if (lookUpDT.Rows.Count > 0)
                            {
                                queryText = BuildLookUpQuery(Fields, lookUpDT, componentName);
                                queryText = AddUserAttributes(queryText, user);
                                field.LookUpQuery = queryText;
                                lookUpDT = (_commonServices.getDataTableFromQuery(field.LookUpQuery.ToString(), cmd, connectionString) as DataTable);
                                field.LookUpValues = _commonServices.GetLookupListFormDataTable(lookUpDT);
                            }
                        }
                    }
                    else
                    {
                        throw new Exception("LookUp : LookUp does Not Exists");
                    }
                }
                if (field.Visible)
                {
                    if (field.IsCalc)
                    {
                        if (field.CalcExpr.ToLower().Contains("userid()"))
                        {
                            field.CalcExpr = CommonServices.ReplaceCaseInsensitive(field.CalcExpr, "userid()", $"'{user.Id}'");
                        }
                        if (field.CalcExpr.ToLower().Contains("groupid()"))
                        {
                            field.CalcExpr = CommonServices.ReplaceCaseInsensitive(field.CalcExpr, "groupid()", $"'{user.UserGroupId}'");
                        }
                        if (field.CalcExpr.ToLower().Contains("roleid()"))
                        {
                            field.CalcExpr = CommonServices.ReplaceCaseInsensitive(field.CalcExpr, "roleid()", $"'{user.RoleId}'");
                        }
                        if (field.CalcExpr.ToLower().Contains("langid()"))
                        {
                            field.CalcExpr = CommonServices.ReplaceCaseInsensitive(field.CalcExpr, "langid()", $"'{user.Language}'");
                        }
                        tableColumns += $"({field.CalcExpr}) {field.Name},";
                    }
                    else if (field.Type == "Date")
                    {
                        tableColumns += $"Convert(Nvarchar, {component.TableName}.{field.ColumnName}, 20) {field.Name},";
                    }
                    else if (field.Type == "DateTime")
                    {
                        tableColumns += $"FORMAT ({component.TableName}.{field.ColumnName}, 'dd/MM/yyyy hh:mm tt', 'en-US') {field.Name},";
                    }
                    else if (field.Type == "Time")
                    {
                        tableColumns += $"FORMAT ({component.TableName}.{field.ColumnName}, 'hh:mm tt', 'en-US') {field.Name},";
                    }
                    else
                        tableColumns += $"{component.TableName}.{field.ColumnName} {field.Name},";
                }

                Fields.Add(field);
            }
            queryText = $"SELECT {tableColumns.TrimEnd(',')} FROM {component.TableName} Where id = @ActiveRow";


            if (activeRow == "-1")
            {
                foreach (var field in Fields)
                {
                    if (field.DefaultValue != null && field.DefaultValue.ToString() != "")
                    {
                        cmd.Parameters.Clear();

                        if (field.DefaultValue.ToString().ToLower() == "userid()")
                        {
                            field.Value = user.Id;
                        }
                        if (field.DefaultValue.ToString().ToLower() == "groupid()")
                        {
                            field.Value = user.UserGroupId;
                        }
                        if (field.DefaultValue.ToString().ToLower() == "roleid()")
                        {
                            field.Value = user.RoleId;
                        }
                        if (field.DefaultValue.ToString().ToLower() == "langid()")
                        {
                            field.Value = user.Language;
                        }
                        if (field.DefaultValue.ToString().Contains("[") && field.DefaultValue.ToString().Contains("]"))
                        {
                            // read from other field value
                        }
                        else
                        {

                            field.Value = _commonServices.ExecuteQuery_OneValue($"SELECT {field.DefaultValue}", cmd, connectionString);
                        }
                    }
                }
            }
            else
            {
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@ActiveRow", activeRow);

                var dt = _commonServices.getDataTableFromQuery(queryText, cmd, connectionString, CommandType.Text);

                for (int i = 0; i < dt.Columns.Count; i++)
                {
                    var field = Fields.Find(x => x.Name == dt.Columns[i].ColumnName);
                    field.Value = dt.Rows[0][dt.Columns[i].ColumnName];
                }
            }

            return Fields;
        }

        public string AddUserAttributes(string queryText, Users user)
        {

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

        public async Task<List<ComponentFieldsDto>> GetComponentFields(string componentName)
        {
            SqlCommand cmd = new SqlCommand();
            var queryText = "Select Component.Name ComponentName, Component.Title ComponentTitle, Component.TableName ComponentTable, Component.RowsCount PageSize, ComponentField.* " +
                    "From [dbo].[Component] , [dbo].[ComponentField]" +
                    "Where Component.Id = ComponentField.ComponentId And Component.Name = @ComponentName" +
                    " Order by DefaultValue ";
            cmd.Parameters.AddWithValue("@ComponentName", componentName);
            var dataTable = new List<ComponentFieldsDto>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var dto = new ComponentFieldsDto
                {
                    // 🎯 Component (C.*)
                    ComponentName = reader["ComponentName"]?.ToString() ?? "",
                    ComponentTitle = reader["ComponentTitle"]?.ToString() ?? "",
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
                    Lable = reader["Lable"]?.ToString() ?? "",
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

                dataTable.Add(dto);
            }
            return dataTable;
        }

        private string BuildLookUpQuery(List<Fields> fields, DataTable lookUpDT, string componentName)
        {
            string[] separatingChars = { "[", "]" };
            List<SearchField> searchFieldList = new List<SearchField>();
            SearchField searchField;

            var fieldCode = lookUpDT.Select($"id = '{lookUpDT.Rows[0]["FieldCode"]}'")[0]["ColumnName"];
            var fieldName = lookUpDT.Select($"id = '{lookUpDT.Rows[0]["FieldValue"]}'")[0]["ColumnName"];
            var tableName = lookUpDT.Rows[0]["TableName"];
            var searchSpec = lookUpDT.Rows[0]["SearchSpec"];
            var queryText = "";

            string searchString = searchSpec.ToString().Trim();
            string[] searchFields = searchString.Split(separatingChars, StringSplitOptions.RemoveEmptyEntries);
            string[] staticList = { "=", ">", "<", "<>", "and", "or", "between", "(", ")" };


            if (searchSpec != null && searchSpec.ToString() != "")
            {

                for (int s = 0; s < searchFields.Length; s++)
                {
                    searchField = new SearchField();

                    if (searchFields[s] == "P")
                    {
                        searchField.FieldName = searchFields[s + 1];
                        searchField.Type = "Parent";
                        s++;
                    }
                    else if (searchFields[s] == "S")
                    {
                        searchField.FieldName = searchFields[s + 1];
                        searchField.Type = "Session";
                        s++;
                    }
                    else if (staticList.Any(searchFields[s].Trim().Contains))
                    {
                        searchField.FieldName = searchFields[s];
                        searchField.FieldValue = searchFields[s];
                        searchField.Type = "Static";
                    }
                    else
                    {
                        searchField.FieldName = searchFields[s];
                        searchField.Type = "Field";
                    }
                    searchFieldList.Add(searchField);
                }

                searchString = "";
                foreach (SearchField sf in searchFieldList)
                {
                    if (sf.Type == "Parent")
                    {
                        string x = fields.Find(x => x.Name == sf.FieldName).Value.ToString();

                        searchString += " " + ((x == "") ? "NULL" : x) + " ";
                    }
                    else if (sf.Type == "Field")
                    {
                        DataRow[] dr = lookUpDT.Select("Name = '" + sf.FieldName + "'");
                        if (dr.Length == 0)
                            throw new Exception($"LookUp Search Spec Error: {sf.FieldName} Does not Exists in Component " + componentName);
                        searchString += " " + dr[0]["ColumnName"].ToString() + " ";
                    }
                    else if (sf.Type == "Session")
                    {
                        // data must be get from session and this will be proccessed later
                        searchString += " ";
                    }
                    else
                    {
                        searchString += " " + sf.FieldValue + " ";
                    }
                }
            }

            queryText = $"SELECT TOP(500) {fieldCode} Code, {fieldName} Name From {tableName}";
            queryText += (searchString != "") ? $" WHERE {searchString}" : "";

            return queryText;
        }

        #endregion
    }
}
