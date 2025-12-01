using DomainServices.Data;
using DomainServices.Data.Repository;
using DomainServices.Models;
using DomainServices.Models.Core;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using Component = DomainServices.Models.Component;

namespace DomainServices.Services
{
    public class CoreData
    {
        #region Fields

        private readonly CommonServices _commonServices;
        private readonly DomainDBContext.DomainRepo _domainRepo;
        public SharedVariable sharedVariable;

        #endregion

        #region Constructor

        public CoreData(CommonServices commonServices, DomainDBContext.DomainRepo domainRepo)
        {
            _commonServices = commonServices;
            _domainRepo = domainRepo;
            sharedVariable = new SharedVariable();
        }

        #endregion

        #region Methods

        public async Task<List<Fields>> InitFieldsStrong(Component component, string selectedId, Users user)
        {
            var result = new List<Fields>();
            var rows = await GetComponentFields(component.Name); // strong metadata
            var connectionString = _commonServices.getConnectionString();
            var cmd = new SqlCommand();

            // ---------------------------------------------------
            // NEW ROW → only defaults (no DB read)
            // ---------------------------------------------------
            if (selectedId == "-1")
            {
                foreach (var row in rows)
                {
                    var f = new Fields
                    {
                        Name = row.Name,
                        ColumnName = row.ColumnName,
                        Type = row.DataType,
                        Visible = row.DisplayInForm,
                        Required = row.Required,
                        ReadOnly = row.ReadOnly,
                        DefaultValue = row.DefaultValue,
                        FieldSize = row.FieldSize ?? 12,

                        // extra meta
                        ImmediatePost = row.ImmediatePost,
                        DisplayInPopup = row.DisplayInPopup,
                        IsCalc = row.IsCalc,
                        CalcExpr = row.CalcExpr,
                        FileDataColumn = row.FileDataColumn,
                        LookUpQuery = row.LookUp?.ToString(),
                        label = row.Lable ?? ""
                    };

                    // Resolve default value → Value
                    if (!string.IsNullOrWhiteSpace(row.DefaultValue))
                    {
                        string dv = row.DefaultValue.Trim().ToLowerInvariant();

                        if (dv == "userid()")
                            f.Value = user.Id;
                        else if (dv == "groupid()")
                            f.Value = user.UserGroupId;
                        else if (dv == "roleid()")
                            f.Value = user.RoleId;
                        else if (dv == "langid()")
                            f.Value = user.Language;
                        else if (dv.Contains("[") && dv.Contains("]"))
                        {
                            // TODO: handle [OtherField] expression if you use it
                        }
                        else
                        {
                            f.Value = _commonServices.ExecuteQuery_OneValue(
                                $"SELECT {row.DefaultValue}", null, connectionString);
                        }
                    }

                    result.Add(f);
                }

                return result;
            }

            // ---------------------------------------------------
            // EDIT ROW → build SELECT + load row from DB
            // ---------------------------------------------------
            var selectColumns = new StringBuilder();

            foreach (var row in rows)
            {
                if (!row.DisplayInForm)
                    continue;

                string alias = row.Name;
                string expr;

                if (row.IsCalc && !string.IsNullOrEmpty(row.CalcExpr))
                {
                    var e = row.CalcExpr;

                    e = CommonServices.ReplaceCaseInsensitive(e, "userid()", $"'{user.Id}'");
                    e = CommonServices.ReplaceCaseInsensitive(e, "groupid()", $"'{user.UserGroupId}'");
                    e = CommonServices.ReplaceCaseInsensitive(e, "roleid()", $"'{user.RoleId}'");
                    e = CommonServices.ReplaceCaseInsensitive(e, "langid()", $"'{user.Language}'");

                    expr = $"({e})";
                }
                else
                {
                    expr = $"{component.TableName}.{row.ColumnName}";
                }

                // same formatting rules you used before
                if (row.DataType.Equals("Date", StringComparison.OrdinalIgnoreCase))
                    expr = $"CONVERT(NVARCHAR, {expr}, 20)";
                else if (row.DataType.Equals("DateTime", StringComparison.OrdinalIgnoreCase))
                    expr = $"FORMAT({expr}, 'dd/MM/yyyy hh:mm tt', 'en-US')";
                else if (row.DataType.Equals("Time", StringComparison.OrdinalIgnoreCase))
                    expr = $"FORMAT({expr}, 'hh:mm tt', 'en-US')";

                selectColumns.Append($"{expr} AS {alias},");
            }

            var selectClause = selectColumns.ToString().TrimEnd(',');
            var sql = $"SELECT {selectClause} FROM {component.TableName} WHERE Id = @Id";

            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@Id", selectedId);

            var dbRow = await _commonServices.ExecuteSingleRow(sql, cmd, connectionString);

            foreach (var row in rows)
            {
                var f = new Fields
                {
                    Name = row.Name,
                    ColumnName = row.ColumnName,
                    Type = row.DataType,
                    Visible = row.DisplayInForm,
                    Required = row.Required,
                    ReadOnly = row.ReadOnly,
                    DefaultValue = row.DefaultValue,
                    FieldSize = row.FieldSize ?? 12,

                    ImmediatePost = row.ImmediatePost,
                    DisplayInPopup = row.DisplayInPopup,
                    IsCalc = row.IsCalc,
                    CalcExpr = row.CalcExpr,
                    FileDataColumn = row.FileDataColumn,
                    LookUpQuery = row.LookUp?.ToString(),
                    label = row.Lable ?? "",

                    Value = (dbRow != null && dbRow.TryGetValue(row.Name, out var val)) ? val : null
                };

                result.Add(f);
            }

            return result;
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
            var result = new List<ComponentFieldsDto>();

            var sql = @"
        SELECT 
            C.Name       AS ComponentName,
            C.Title      AS ComponentTitle,
            C.TableName  AS ComponentTable,
            C.RowsCount  AS PageSize,
            C.OnCreateProc,
            C.OnUpdateProc,
            C.OnDeleteProc,
            C.SearchSpec,
            C.SortSpec,
            C.Type,
            CF.*
        FROM Component C
        JOIN ComponentField CF ON C.Id = CF.ComponentId
        WHERE C.Name = @ComponentName
        ORDER BY CF.DisplaySequence;
    ";

            using (var conn = new SqlConnection(_commonServices.getConnectionString()))
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@ComponentName", componentName);

                await conn.OpenAsync();
                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var dto = new ComponentFieldsDto
                    {
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

                    result.Add(dto);
                }
            }

            return result;
        }

        #region LoadData - Helpers  
        // ----------------------------------------------
        // SQL → List<Dictionary<string,object>>
        // ----------------------------------------------
        public List<Dictionary<string, object>> ExecuteListDictionary(
            string query,
            SqlCommand cmd,
            string connectionString)
        {
            var list = new List<Dictionary<string, object>>();

            using (SqlConnection con = new SqlConnection(connectionString))
            {
                cmd.Connection = con;
                cmd.CommandText = query;

                con.Open();

                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            object value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                            row[reader.GetName(i)] = value;
                        }

                        list.Add(row);
                    }
                }
            }

            return list;
        }

        // ----------------------------------------------
        // Convert SqlCommand → byte[] (for caching)
        // ----------------------------------------------
        public byte[] ConvertSqlCommandToByte(SqlCommand cmd)
        {
            var snapshot = new SerializableSqlCommand
            {
                CommandText = cmd.CommandText,
                Parameters = cmd.Parameters
                    .Cast<SqlParameter>()
                    .Select(p => new SerializableSqlParam
                    {
                        Name = p.ParameterName,
                        Type = p.SqlDbType,
                        Value = p.Value
                    }).ToList()
            };

            return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(snapshot));
        }

        // ----------------------------------------------
        // Convert byte[] → SqlCommand
        // ----------------------------------------------
        public SqlCommand GetSqlCommanFromByte(byte[] data)
        {
            if (data == null || data.Length == 0)
                return null;

            var json = Encoding.UTF8.GetString(data);
            var snapshot = JsonConvert.DeserializeObject<SerializableSqlCommand>(json);

            var cmd = new SqlCommand();
            cmd.CommandText = snapshot.CommandText;

            foreach (var p in snapshot.Parameters)
            {
                cmd.Parameters.Add(new SqlParameter(p.Name, p.Type) { Value = p.Value });
            }

            return cmd;
        }

        // Serializable wrapper
        class SerializableSqlCommand
        {
            public string CommandText { get; set; }
            public List<SerializableSqlParam> Parameters { get; set; }
        }
        class SerializableSqlParam
        {
            public string Name { get; set; }
            public SqlDbType Type { get; set; }
            public object Value { get; set; }
        }

        // ----------------------------------------------
        // Replace Calc Defaults (user attributes)
        // ----------------------------------------------
        public string ReplaceCalcDefaults(string expr, Users user)
        {
            if (string.IsNullOrEmpty(expr)) return expr;

            return expr
                .Replace("UserId()", $"'{user.Id}'")
                .Replace("UserGroupId()", user.UserGroupId.ToString())
                .Replace("LangId()", $"'{user.Language}'");
        }

        // ----------------------------------------------
        // BUILD SEARCH CONDITIONS
        // ----------------------------------------------
        public string BuildSearchConditions(
            Dictionary<string, string> search,
            List<ComponentDetail> meta,
            SqlCommand cmd)
        {
            if (search == null || search.Count == 0)
                return "";

            string result = "";

            foreach (var item in search)
            {
                string key = item.Key;
                string userValue = item.Value;

                if (string.IsNullOrEmpty(userValue)) continue;

                string dateSide = "";
                if (key.Contains("_From"))
                {
                    key = key.Replace("_From", "");
                    dateSide = "From";
                }
                else if (key.Contains("_To"))
                {
                    key = key.Replace("_To", "");
                    dateSide = "To";
                }

                var field = meta.FirstOrDefault(f => f.FieldName == key);
                if (field == null) continue;

                result += (result != "") ? " AND " : "";

                string left = field.IsCalc ? field.CalcExpression : $"{field.TableName}.{field.TableColumn}";
                result += left;

                if (dateSide == "From") result += " >= ";
                else if (dateSide == "To") result += " <= ";
                else
                {
                    result += field.DataType.ToLower() == "text"
                        ? " LIKE "
                        : " = ";
                }

                // param
                string param = item.Key;

                if (field.DataType.ToLower() == "checkbox")
                    userValue = (userValue == "true") ? "1" : "0";

                if (dateSide == "To")
                    userValue += " 23:59:59";

                cmd.Parameters.AddWithValue(param,
                    field.DataType.ToLower() == "text"
                        ? $"%{userValue}%"
                        : userValue);
            }

            return result;
        }

        // ----------------------------------------------
        // BUILD FINAL QUERY (fast)
        // ----------------------------------------------
        public void BuildLoadDataQuery(
            Component component,
            LoadViewListItemDto view,
            Dictionary<string, string> search,
            Users user,
            SqlCommand cmd,
            out string query,
            out string queryCount)
        {
            string connectionString = _commonServices.getConnectionString();

            // ---- Load fields for component (fast query)
            string sql = @"
            SELECT 
                Name, TableName, ColumnName, DataType,
                ReadOnly, Required, DisplayInForm, DisplayInList,
                IsCalc, CalcExpr, LookUp
            FROM ComponentField
            WHERE ComponentId = (SELECT Id FROM Component WHERE Name = @Name)
            ORDER BY DisplaySequence";

            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@Name", component.Name);

            var rows = ExecuteListDictionary(sql, cmd, connectionString);

            var fields = rows.Select(r => new ComponentDetail
            {
                FieldName = r["Name"]?.ToString() ?? "",
                TableName = (r["TableName"]?.ToString() ?? "").ToLower() == "transactions"
                            ? component.TableName
                            : r["TableName"]?.ToString(),
                TableColumn = r["ColumnName"]?.ToString() ?? "",
                DataType = r["DataType"]?.ToString() ?? "",
                DisplayInList = Convert.ToBoolean(r["DisplayInList"]),
                IsCalc = Convert.ToBoolean(r["IsCalc"]),
                CalcExpression = r["CalcExpr"]?.ToString() ?? "",
                LookUp = r["LookUp"]?.ToString() ?? "-1"
            }).ToList();

            var sbSelect = new StringBuilder();
            foreach (var f in fields.Where(f => f.DisplayInList))
            {
                string col =
                    (f.IsCalc && !string.IsNullOrEmpty(f.CalcExpression))
                    ? "(" + ReplaceCalcDefaults(f.CalcExpression, user) + ")"
                    : $"{f.TableName}.{f.TableColumn}";

                sbSelect.Append($"{col} {f.FieldName},");
            }

            string selectColumns = sbSelect.ToString().TrimEnd(',');

            // ---- access filter
            string whereAccess = view.ViewDataAccess switch
            {
                1 => "",
                2 => $" GroupId IN (SELECT ID FROM dbo.GetUserGroups('{user.Id}')) ",
                _ => $" CreatedBy = '{user.Id}' "
            };

            // ---- search conditions
            string whereSearch = BuildSearchConditions(search, fields, cmd);

            // ---- final WHERE
            string whereFinal =_commonServices.GetQueryWhere(
                view.CompFieldName,
                whereSearch,
                whereAccess,
                component.SearchSpec,
                user
            );

            // ---- Final SELECT
            query =
                $"SELECT {selectColumns} FROM {component.TableName} " +
                $"{whereFinal} " +
                $" ORDER BY {component.TableName}.{component.SortSpec} " +
                " OFFSET @PageSize * (@PageNumber - 1) ROWS " +
                " FETCH NEXT @PageSize ROWS ONLY OPTION (RECOMPILE)";

            query = AddUserAttributes(query, user);

            // ---- Count Query
            queryCount =
                $"SELECT COUNT(1) FROM {component.TableName} {whereFinal}";
        }
        #endregion

        #endregion
    }
}
public static class CoreDataShapeMapper
{
    private static readonly List<ColumnShape> ViewColumns = new()
    {
        new ColumnShape { Name = "Seq", DataType = "decimal" },
        new ColumnShape { Name = "ClientURL", DataType = "string" },
        new ColumnShape { Name = "ViewTitle", DataType = "string" },
        new ColumnShape { Name = "ViewId", DataType = "decimal" },
        new ColumnShape { Name = "ViewStyle", DataType = "int" },
        new ColumnShape { Name = "ViewDataAccess", DataType = "int" },

        new ColumnShape { Name = "CompId", DataType = "decimal" },
        new ColumnShape { Name = "CompName", DataType = "string" },
        new ColumnShape { Name = "CompTitle", DataType = "string" },

        new ColumnShape { Name = "CompFieldId", DataType = "decimal?" },
        new ColumnShape { Name = "ParCompId", DataType = "decimal?" },
        new ColumnShape { Name = "ParCompFieldId", DataType = "decimal?" },

        new ColumnShape { Name = "ReadOnly", DataType = "bool" },
        new ColumnShape { Name = "ExportExcel", DataType = "bool" },
        new ColumnShape { Name = "NoInsert", DataType = "bool" },
        new ColumnShape { Name = "NoUpdate", DataType = "bool" },
        new ColumnShape { Name = "NoDelete", DataType = "bool" },

        new ColumnShape { Name = "CompFieldName", DataType = "string" },
        new ColumnShape { Name = "ParCompName", DataType = "string" },
        new ColumnShape { Name = "ParCompFieldName", DataType = "string" },

        new ColumnShape { Name = "ParCompFieldValue", DataType = "string" },

        new ColumnShape { Name = "HasDetail", DataType = "int" },
        new ColumnShape { Name = "ViewDescription", DataType = "string" },

        new ColumnShape { Name = "QueryString", DataType = "string" },
        new ColumnShape { Name = "QueryStringCount", DataType = "string" },
        new ColumnShape { Name = "QueryStringCmd", DataType = "string" },

        new ColumnShape { Name = "Component", DataType = "object" }
    };


    // --------------------------
    //  Convert LIST -> DataShape
    // --------------------------
    public static DataShape ToShape(List<LoadViewListItemDto> list)
    {
        return new DataShape
        {
            Columns = ViewColumns,

            Rows = list.Select(x => new Dictionary<string, object>
            {
                ["Seq"] = x.Seq,
                ["ClientURL"] = x.ClientURL,
                ["ViewTitle"] = x.ViewTitle,
                ["ViewId"] = x.ViewId,
                ["ViewStyle"] = x.ViewStyle,
                ["ViewDataAccess"] = x.ViewDataAccess,

                ["CompId"] = x.CompId,
                ["CompName"] = x.CompName,
                ["CompTitle"] = x.CompTitle,

                ["CompFieldId"] = x.CompFieldId,
                ["ParCompId"] = x.ParCompId,
                ["ParCompFieldId"] = x.ParCompFieldId,

                ["ReadOnly"] = x.ReadOnly,
                ["ExportExcel"] = x.ExportExcel,
                ["NoInsert"] = x.NoInsert,
                ["NoUpdate"] = x.NoUpdate,
                ["NoDelete"] = x.NoDelete,

                ["CompFieldName"] = x.CompFieldName,
                ["ParCompName"] = x.ParCompName,
                ["ParCompFieldName"] = x.ParCompFieldName,
                ["ParCompFieldValue"] = x.ParCompFieldValue,

                ["HasDetail"] = x.HasDetail,
                ["ViewDescription"] = x.ViewDescription,

                ["QueryString"] = x.QueryString,
                ["QueryStringCount"] = x.QueryStringCount,
                ["QueryStringCmd"] = x.QueryStringCmd,

                ["Component"] = x.Component
            }).ToList()
        };
    }


    // --------------------------
    //  Convert DataShape -> LIST
    // --------------------------
    public static List<LoadViewListItemDto> FromShape(DataShape shape)
    {
        var list = new List<LoadViewListItemDto>();

        foreach (var r in shape.Rows)
        {
            var x = new LoadViewListItemDto
            {
                Seq = Convert.ToDecimal(r["Seq"]),
                ClientURL = r["ClientURL"]?.ToString() ?? "",
                ViewTitle = r["ViewTitle"]?.ToString() ?? "",
                ViewId = Convert.ToDecimal(r["ViewId"]),
                ViewStyle = Convert.ToInt32(r["ViewStyle"]),
                ViewDataAccess = Convert.ToInt32(r["ViewDataAccess"]),

                CompId = Convert.ToDecimal(r["CompId"]),
                CompName = r["CompName"]?.ToString() ?? "",
                CompTitle = r["CompTitle"]?.ToString() ?? "",

                CompFieldId = r["CompFieldId"] as decimal?,
                ParCompId = r["ParCompId"] as decimal?,
                ParCompFieldId = r["ParCompFieldId"] as decimal?,

                ReadOnly = Convert.ToBoolean(r["ReadOnly"]),
                ExportExcel = Convert.ToBoolean(r["ExportExcel"]),
                NoInsert = Convert.ToBoolean(r["NoInsert"]),
                NoUpdate = Convert.ToBoolean(r["NoUpdate"]),
                NoDelete = Convert.ToBoolean(r["NoDelete"]),

                CompFieldName = r["CompFieldName"]?.ToString() ?? "",
                ParCompName = r["ParCompName"]?.ToString() ?? "",
                ParCompFieldName = r["ParCompFieldName"]?.ToString() ?? "",
                ParCompFieldValue = r.ContainsKey("ParCompFieldValue")
                    ? r["ParCompFieldValue"]?.ToString() ?? ""
                    : "",

                HasDetail = Convert.ToInt32(r["HasDetail"]),
                ViewDescription = r["ViewDescription"]?.ToString() ?? "",

                QueryString = r["QueryString"]?.ToString() ?? "",
                QueryStringCount = r["QueryStringCount"]?.ToString() ?? "",
                QueryStringCmd = r["QueryStringCmd"]?.ToString() ?? "",

                Component = JsonConvert.DeserializeObject<Component>(JsonConvert.SerializeObject(r["Component"]))
            };

            list.Add(x);
        }

        return list;
    }
}
