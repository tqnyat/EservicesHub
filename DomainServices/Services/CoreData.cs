using DomainServices.Data;
using DomainServices.Data.Repository;
using DomainServices.Models;
using DomainServices.Models.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Component = DomainServices.Models.Component;

namespace DomainServices.Services
{
    public class CoreData
    {
        #region Fields

        private readonly CommonServices _commonServices;
        private readonly DomainDBContext.DomainRepo _domainRepo;

        #endregion

        #region Constructor

        public CoreData(CommonServices commonServices, DomainDBContext.DomainRepo domainRepo)
        {
            _commonServices = commonServices;
            _domainRepo = domainRepo;
        }

        #endregion

        #region Methods

        public async Task<List<Fields>> InitFieldsStrong(Component component, string selectedId, Users user)
        {
            var result = new List<Fields>();
            var rows = await GetComponentFields(component.Name);
            var connectionString = _commonServices.getConnectionString();

            // ----------------------------------------
            // NEW ROW CASE (selectedId = -1)
            // ----------------------------------------
            if (selectedId == "-1")
            {
                foreach (var row in rows)
                {
                    var f = BuildFieldMetadata(row, user);

                    // -----------------------------
                    // Build lookup values once
                    // -----------------------------
                    if (!string.IsNullOrEmpty(f.LookUpQuery))
                    {
                        ApplyLookupValues(f, user, connectionString);
                    }

                    // -----------------------------
                    // Apply default values only
                    // -----------------------------
                    if (!string.IsNullOrWhiteSpace(row.DefaultValue))
                        f.Value = ResolveDefaultValue(row.DefaultValue, user, connectionString);

                    result.Add(f);
                }

                return result;
            }

            // ----------------------------------------
            // EDIT MODE → Must fetch row values
            // ----------------------------------------
            // Build SELECT list
            var sbSelect = new StringBuilder();

            foreach (var row in rows)
            {
                if (!row.DisplayInForm)
                    continue;

                string alias = row.Name;
                string expr;

                if (row.IsCalc && !string.IsNullOrEmpty(row.CalcExpr))
                {
                    expr = ApplyCalcExpr(row.CalcExpr, user);
                }
                else
                {
                    expr = $"{component.TableName}.{row.ColumnName}";
                }

                // Type formatting
                if (row.DataType.Equals("Date", StringComparison.OrdinalIgnoreCase))
                    expr = $"CONVERT(NVARCHAR, {expr}, 20)";
                else if (row.DataType.Equals("DateTime", StringComparison.OrdinalIgnoreCase))
                    expr = $"FORMAT({expr}, 'dd/MM/yyyy hh:mm tt', 'en-US')";
                else if (row.DataType.Equals("Time", StringComparison.OrdinalIgnoreCase))
                    expr = $"FORMAT({expr}, 'hh:mm tt', 'en-US')";

                sbSelect.Append($"{expr} AS {alias},");
            }

            string selectClause = sbSelect.ToString().TrimEnd(',');
            string sql = $"SELECT {selectClause} FROM {component.TableName} WHERE Id = @Id";

            var cmd = new SqlCommand();
            cmd.Parameters.AddWithValue("@Id", selectedId);

            // get 1 record
            var dbRow = await _commonServices.ExecuteSingleRow(sql, cmd, connectionString);

            // ----------------------------------------
            // Build fields (never overwrite metadata)
            // ----------------------------------------
            foreach (var row in rows)
            {
                // good fields always come from metadata
                var f = BuildFieldMetadata(row, user);

                // lookup values (unchanged in edit mode)
                if (!string.IsNullOrEmpty(f.LookUpQuery))
                    ApplyLookupValues(f, user, connectionString);

                // apply DB value only
                if (dbRow != null && dbRow.ContainsKey(row.Name))
                    f.Value = dbRow[row.Name];

                result.Add(f);
            }

            return result;
        }
        private Fields BuildFieldMetadata(ComponentFieldsDto row, Users user)
        {
            return new Fields
            {
                Name = row.Name,
                ColumnName = row.ColumnName,
                DefaultValue = row.DefaultValue,
                Visible = row.DisplayInForm,
                ReadOnly = row.ReadOnly,
                Required = row.Required,
                Type = row.DataType,
                LookUpQuery = row.LookUp?.ToString(),
                ImmediatePost = row.ImmediatePost,
                DisplayInPopup = row.DisplayInPopup,
                IsCalc = row.IsCalc,
                CalcExpr = row.CalcExpr,
                FileDataColumn = row.FileDataColumn,
                FileDataValue = "",
                FieldSize = row.FieldSize ?? 12,
                label = row.Lable ?? ""
            };
        }
        private void ApplyLookupValues(Fields f, Users user, string connectionString)
        {
            var cmd = new SqlCommand();

            // Load lookup header
            string headerSql = "SELECT * FROM LookUps WHERE Id = @Id";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@Id", f.LookUpQuery);

            DataShape headerShape =
                _commonServices.ExecuteQuery_DataShape(headerSql, cmd, connectionString);

            if (headerShape.Rows.Count == 0)
                throw new Exception("LookUp does not exist.");

            var header = headerShape.Rows[0];
            var lookupType = header["Type"]?.ToString() ?? "0";

            // ----------------------------------
            // Lookup Type = 1 → LookUpDetail
            // ----------------------------------
            if (lookupType == "1")
            {
                f.Type = "lookup-1";

                string col = user.Language.Contains("en") ? "Name" : "NameAr";

                string sql =
                    $"SELECT TOP 500 {col} AS Name, Code FROM LookUpDetail WHERE LookUpId = @LID";

                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@LID", header["Id"]);

                DataShape values =
                    _commonServices.ExecuteQuery_DataShape(sql, cmd, connectionString);

                f.LookUpQuery = sql;
                f.LookUpValues = _commonServices.GetLookupListFormDataShape(values);
            }

            // ----------------------------------
            // Lookup Type = 2 → Component-based
            // ----------------------------------
            else if (lookupType == "2")
            {
                f.Type = "lookup-1";

                string sqlMeta =
                    "SELECT C.Id, C.Name, C.TableName, " +
                    "CASE WHEN IsCalc = 1 THEN C.CalcExpr ELSE C.ColumnName END ColumnName, " +
                    "C.CalcExpr, L.FieldCode, L.FieldValue, L.SearchSpec " +
                    "FROM ComponentField C, LookUps L " +
                    "WHERE L.Component = C.ComponentId AND L.Id = @LID";

                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@LID", header["Id"]);

                DataShape meta = _commonServices.ExecuteQuery_DataShape(sqlMeta, cmd, connectionString);

                string finalSql = BuildLookUpQuery(new List<Fields>(), meta, header["Name"]?.ToString());
                finalSql = AddUserAttributes(finalSql, user);

                f.LookUpQuery = finalSql;

                DataShape values =
                    _commonServices.ExecuteQuery_DataShape(finalSql, cmd, connectionString);

                f.LookUpValues = _commonServices.GetLookupListFormDataShape(values);
            }
        }
        private object ResolveDefaultValue(string dv, Users user, string connectionString)
        {
            dv = dv.Trim().ToLower();

            if (dv == "userid()") return user.Id;
            if (dv == "groupid()") return user.UserGroupId;
            if (dv == "roleid()") return user.RoleId;
            if (dv == "langid()") return user.Language;

            if (dv.Contains("[") && dv.Contains("]"))
                return null; // unchanged legacy

            return _commonServices.ExecuteQuery_OneValue($"SELECT {dv}", null, connectionString);
        }
        private string ApplyCalcExpr(string expr, Users user)
        {
            if (expr.Contains("userid()", StringComparison.OrdinalIgnoreCase))
                expr = CommonServices.ReplaceCaseInsensitive(expr, "userid()", $"'{user.Id}'");

            if (expr.Contains("groupid()", StringComparison.OrdinalIgnoreCase))
                expr = CommonServices.ReplaceCaseInsensitive(expr, "groupid()", $"'{user.UserGroupId}'");

            if (expr.Contains("roleid()", StringComparison.OrdinalIgnoreCase))
                expr = CommonServices.ReplaceCaseInsensitive(expr, "roleid()", $"'{user.RoleId}'");

            if (expr.Contains("langid()", StringComparison.OrdinalIgnoreCase))
                expr = CommonServices.ReplaceCaseInsensitive(expr, "langid()", $"'{user.Language}'");

            return $"({expr})";
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
        public string BuildLookUpQuery(List<Fields> fields, DataShape lookUpShape, string componentName)
        {
            // -----------------------------------------
            // Extract first lookup row
            // -----------------------------------------
            if (lookUpShape?.Rows == null || lookUpShape.Rows.Count == 0)
                throw new Exception("LookUp: LookUps table is empty.");

            var first = lookUpShape.Rows[0];

            var fieldCodeId = first["FieldCode"]?.ToString();
            var fieldValueId = first["FieldValue"]?.ToString();
            var tableName = first["TableName"]?.ToString();
            var searchSpec = first["SearchSpec"]?.ToString();

            if (string.IsNullOrWhiteSpace(fieldCodeId) ||
                string.IsNullOrWhiteSpace(fieldValueId) ||
                string.IsNullOrWhiteSpace(tableName))
                throw new Exception("LookUp: Missing mandatory fields (FieldCode, FieldValue, TableName).");

            // -----------------------------------------
            // Resolve ColumnName for FieldCode + FieldValue
            // -----------------------------------------
            string fieldCode = null;
            string fieldName = null;

            foreach (var row in lookUpShape.Rows)
            {
                if (row["Id"]?.ToString() == fieldCodeId)
                    fieldCode = row["ColumnName"]?.ToString();

                if (row["Id"]?.ToString() == fieldValueId)
                    fieldName = row["ColumnName"]?.ToString();
            }

            if (fieldCode == null || fieldName == null)
                throw new Exception("LookUp: Could not resolve lookup code/name column mapping.");

            // -----------------------------------------
            // Parse SearchSpec into SQL
            // -----------------------------------------
            string sqlWhere = "";
            if (!string.IsNullOrWhiteSpace(searchSpec))
            {
                // Split tokens: same legacy behavior
                string[] tokens = searchSpec.Split(
                    new string[] { "[", "]" },
                    StringSplitOptions.RemoveEmptyEntries);

                string[] staticOps = { "=", ">", "<", "<>", "and", "or", "between", "(", ")" };
                var searchParts = new List<SearchField>();

                for (int i = 0; i < tokens.Length; i++)
                {
                    var token = tokens[i].Trim();
                    var sf = new SearchField();

                    if (token == "P")
                    {
                        sf.FieldName = tokens[++i];
                        sf.Type = "Parent";
                    }
                    else if (token == "S")
                    {
                        sf.FieldName = tokens[++i];
                        sf.Type = "Session";
                    }
                    else if (staticOps.Contains(token.ToLower()))
                    {
                        sf.FieldName = token;
                        sf.FieldValue = token;
                        sf.Type = "Static";
                    }
                    else
                    {
                        sf.FieldName = token;
                        sf.Type = "Field";
                    }

                    searchParts.Add(sf);
                }

                var sb = new StringBuilder();

                foreach (var sf in searchParts)
                {
                    if (sf.Type == "Parent")
                    {
                        // get value from Fields list
                        var f = fields.FirstOrDefault(z => z.Name == sf.FieldName);
                        if (f == null)
                            throw new Exception($"LookUp SearchSpec Error: Field '{sf.FieldName}' not found.");

                        sb.Append(" ")
                          .Append(string.IsNullOrEmpty(f.Value?.ToString()) ? "NULL" : f.Value)
                          .Append(" ");
                    }
                    else if (sf.Type == "Field")
                    {
                        var compField = fields.FirstOrDefault(z => z.Name == sf.FieldName);
                        if (compField != null)
                        {
                            sb.Append(" ").Append(compField.ColumnName).Append(" ");
                            continue;
                        }

                        sb.Append(" ").Append(sf.FieldName).Append(" ");
                    }
                    else if (sf.Type == "Session")
                    {
                        // not implemented in old version — keep empty
                        sb.Append(" ");
                    }
                    else
                    {
                        sb.Append(" ").Append(sf.FieldValue).Append(" ");
                    }
                }

                sqlWhere = sb.ToString().Trim();
            }

            // -----------------------------------------
            // Build final query
            // -----------------------------------------
            var queryText = new StringBuilder();

            queryText.Append($"SELECT TOP(500) {fieldCode} Code, {fieldName} Name FROM {tableName}");

            if (!string.IsNullOrWhiteSpace(sqlWhere))
                queryText.Append(" WHERE ").Append(sqlWhere);

            return queryText.ToString();
        }

        #region SubmitData - Helpers
        public object NormalizeValue(object raw)
        {
            if (raw == null)
                return null;

            if (raw is JsonElement el)
            {
                switch (el.ValueKind)
                {
                    case JsonValueKind.String:
                        return el.GetString();

                    case JsonValueKind.Number:
                        if (el.TryGetInt64(out var l)) return l;
                        if (el.TryGetDouble(out var d)) return d;
                        return el.GetRawText();

                    case JsonValueKind.True:
                        return true;

                    case JsonValueKind.False:
                        return false;

                    case JsonValueKind.Null:
                    case JsonValueKind.Undefined:
                        return null;

                    default:
                        return el.ToString();
                }
            }

            return raw;
        }
        public object ConvertFieldValue(string? type, object value)
        {
            if (value == null) return null;

            type = type?.ToLower() ?? "";

            switch (type)
            {
                case "checkbox":
                    return Convert.ToBoolean(value);

                case "date":
                    return DateTime.ParseExact(value.ToString(), "dd/MM/yyyy", CultureInfo.InvariantCulture);

                case "datetime":
                    return DateTime.ParseExact(value.ToString(), "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);

                case "time":
                    return DateTime.ParseExact(value.ToString(), "hh:mm tt", CultureInfo.InvariantCulture);

                default:
                    return value;
            }
        }
        public void HandleFileInsert(Fields row, StringBuilder cols, StringBuilder vals)
        {
            if (!string.IsNullOrWhiteSpace(row.FileDataColumn))
            {
                if (row.FileDataColumn == "clear-old-data")
                {
                    cols.Append("'',");
                    vals.Append("@" + row.Name + ",");

                    cols.Append("'',");
                    vals.Append("@" + row.FileDataColumn + ",");
                }
                else
                {
                    cols.Append(row.ColumnName + ",");
                    vals.Append("@" + row.Name + ",");

                    cols.Append(row.FileDataColumn + ",");
                    vals.Append("@" + row.FileDataColumn + ",");
                }
            }
            else
            {
                cols.Append(row.ColumnName + ",");
                vals.Append("@" + row.Name + ",");
            }
        }
        public void HandleFileUpdate(Fields row, StringBuilder set)
        {
            var hasData = !string.IsNullOrWhiteSpace(row.FileDataValue);

            if (string.IsNullOrEmpty(row.FileDataColumn))
            {
                set.Append($"{row.ColumnName} = @{row.Name},");
                return;
            }

            // new or updated file
            if (hasData && row.FileDataValue != "clear-old-file")
            {
                set.Append($"{row.ColumnName} = @{row.Name},");
                set.Append($"{row.FileDataColumn} = @{row.FileDataColumn},");
            }
            else if (hasData && row.FileDataValue == "clear-old-file")
            {
                set.Append($"{row.ColumnName} = @{row.Name},");
                set.Append($"{row.FileDataColumn} = @{row.FileDataColumn},");
            }
            else
            {
                // file not changed → update only file name
                set.Append($"{row.ColumnName} = @{row.Name},");
            }
        }
        public void AddSqlParameter(SqlCommand cmd, Fields row)
        {
            string p = "@" + row.Name;

            if (row.Type?.ToLower() == "file")
            {
                // File main column → string (file name)
                cmd.Parameters.AddWithValue(p,
                    string.IsNullOrWhiteSpace(row.Value?.ToString())
                        ? (object)DBNull.Value
                        : row.Value.ToString());

                // FileDataColumn → bytes
                if (!string.IsNullOrWhiteSpace(row.FileDataColumn))
                {
                    string d = row.FileDataValue ?? "";
                    string p2 = "@" + row.FileDataColumn;

                    if (d == "clear-old-file")
                        cmd.Parameters.AddWithValue(p2, Array.Empty<byte>());
                    else if (!string.IsNullOrWhiteSpace(d))
                        cmd.Parameters.AddWithValue(p2, Convert.FromBase64String(d));
                    else
                        cmd.Parameters.AddWithValue(p2, DBNull.Value);
                }
            }
            else
            {
                cmd.Parameters.AddWithValue(p,
                    row.Value == null || string.IsNullOrWhiteSpace(row.Value.ToString())
                        ? (object)DBNull.Value
                        : row.Value);
            }
        }
        public string BuildFinalSql(string saveType, string tableName, SqlCommand cmd, StringBuilder insertCols, StringBuilder insertVals, StringBuilder updateSet, string parentField, string parentFieldValue, bool hasGroupId, Users user, string selectedId, out string rowId)
        {
            rowId = selectedId;
            string sql = "";

            if (saveType == "new")
            {
                if (parentField != "1" && !string.IsNullOrWhiteSpace(parentFieldValue))
                {
                    insertCols.Append(parentField + ",");
                    insertVals.Append("'" + parentFieldValue + "',");
                }

                insertCols.Append("CreatedBy,LastUpdBy");
                insertVals.Append($"'{user.Id}','{user.Id}'");

                if (!hasGroupId)
                {
                    insertCols.Append(",GroupId");
                    insertVals.Append($",{user.UserGroupId}");
                }

                sql = $"INSERT INTO {tableName} ({insertCols.ToString().TrimEnd(',')}) " +
                      $"VALUES ({insertVals.ToString().TrimEnd(',')}); SELECT SCOPE_IDENTITY();";
            }
            else
            {
                updateSet.Append($"LastUpdBy='{user.Id}',LastUpd=GETDATE()");
                cmd.Parameters.AddWithValue("@SelectedId", selectedId);

                sql = $"UPDATE {tableName} SET {updateSet.ToString().TrimEnd(',')} WHERE Id=@SelectedId; SELECT @SelectedId;";
            }

            return sql;
        }
        public void ExecuteSubmitSql(string sql, SqlCommand cmd, Component component, Users user, string saveType, ref string rowId)
        {
            var con = new SqlConnection(_commonServices.getConnectionString());
            SqlTransaction trx = null;

            try
            {
                con.Open();
                trx = con.BeginTransaction();

                cmd.Connection = con;
                cmd.Transaction = trx;
                cmd.CommandType = CommandType.Text;
                cmd.CommandText = sql;

                var result = cmd.ExecuteScalar();
                if (result != null && result != DBNull.Value)
                    rowId = result.ToString();

                // --- Call stored procedures (legacy)
                if (saveType == "new" && !string.IsNullOrEmpty(component.OnCreateProc))
                {
                    var c2 = new SqlCommand(component.OnCreateProc, con, trx);
                    c2.CommandType = CommandType.StoredProcedure;
                    c2.Parameters.AddWithValue("@RowId", rowId);
                    c2.ExecuteNonQuery();
                }
                else if (saveType == "edit" && !string.IsNullOrEmpty(component.OnUpdateProc))
                {
                    var c2 = new SqlCommand(component.OnUpdateProc, con, trx);
                    c2.CommandType = CommandType.StoredProcedure;
                    c2.Parameters.AddWithValue("@RowId", rowId);
                    c2.ExecuteNonQuery();
                }

                trx.Commit();
            }
            catch
            {
                trx?.Rollback();
                throw;
            }
            finally
            {
                if (con.State != ConnectionState.Closed)
                    con.Close();
            }
        }
        public SubmitDataResultDto FailArabic(string msg, DataShape shape)
        {
            return new SubmitDataResultDto
            {
                ExceptionError = msg,
                UpdatedSession = new Dictionary<string, object>
        {
            { "CoreDataView", JsonConvert.SerializeObject(shape) }
        }
            };
        }

        #endregion
        #region LoadData - Helpers  
        // ----------------------------------------------
        // SQL → List<Dictionary<string,object>>
        // ----------------------------------------------
        public List<Dictionary<string, object>> ExecuteListDictionary(string query, SqlCommand cmd, string connectionString)
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
        public string BuildSearchConditions(Dictionary<string, string> search, List<ComponentDetail> meta, SqlCommand cmd)
        {
            if (search == null || search.Count == 0)
                return "";

            var where = new StringBuilder();

            foreach (var kv in search)
            {
                string rawKey = kv.Key;
                string value = kv.Value;

                if (string.IsNullOrWhiteSpace(value))
                    continue;

                // --------------------------
                // Detect Date From/To
                // --------------------------
                string key = rawKey;
                string dateSide = "";

                if (rawKey.EndsWith("_From"))
                {
                    key = rawKey.Replace("_From", "");
                    dateSide = "From";
                }
                else if (rawKey.EndsWith("_To"))
                {
                    key = rawKey.Replace("_To", "");
                    dateSide = "To";
                }

                // Get field metadata
                var field = meta.FirstOrDefault(x =>
                    x.FieldName.Equals(key, StringComparison.OrdinalIgnoreCase));

                if (field == null) continue;

                // Build SQL LEFT side of condition
                string left = field.IsCalc
                    ? field.CalcExpression
                    : $"{field.TableName}.{field.TableColumn}";

                if (where.Length > 0)
                    where.Append(" AND ");

                where.Append(left);

                // --------------------------
                // Add comparison operators
                // --------------------------
                if (dateSide == "From")
                {
                    where.Append(" >= ");
                }
                else if (dateSide == "To")
                {
                    where.Append(" <= ");
                }
                else
                {
                    // Text type = LIKE
                    if (field.DataType.Equals("Text", StringComparison.OrdinalIgnoreCase) ||
                        field.DataType.Equals("String", StringComparison.OrdinalIgnoreCase))
                    {
                        where.Append(" LIKE ");
                    }
                    else
                    {
                        where.Append(" = ");
                    }
                }

                // --------------------------
                // Prepare value and parameter
                // --------------------------
                string param = $"@{rawKey}";

                // Checkbox → convert true/false to 1/0
                if (field.DataType.Equals("CheckBox", StringComparison.OrdinalIgnoreCase))
                {
                    value = (value == "true") ? "1" : "0";
                }

                // Decimal and Number → convert to decimal
                else if (field.DataType.Equals("Decimal", StringComparison.OrdinalIgnoreCase) ||
                         field.DataType.Equals("Number", StringComparison.OrdinalIgnoreCase))
                {
                    if (decimal.TryParse(value, out decimal dec))
                        value = dec.ToString("0.##"); // for parameter only, select still formats N2
                }

                // LookUp → ID compare (same as old behavior)
                else if (field.DataType.Equals("LookUp", StringComparison.OrdinalIgnoreCase))
                {
                    // value remains ID (lookup text not searchable)
                }

                // DateTo → append 23:59:59
                if (dateSide == "To")
                {
                    value += " 23:59:59";
                }

                // --------------------------
                // Add SQL parameter
                // --------------------------
                if (field.DataType.Equals("Text", StringComparison.OrdinalIgnoreCase) ||
                    field.DataType.Equals("String", StringComparison.OrdinalIgnoreCase))
                {
                    cmd.Parameters.AddWithValue(param, $"%{value}%");
                }
                else
                {
                    cmd.Parameters.AddWithValue(param, value);
                }

                where.Append(param);
            }

            return where.ToString();
        }


        // ----------------------------------------------
        // BUILD FINAL QUERY (fast)
        // ----------------------------------------------
        public void BuildLoadDataQuery(Component component, LoadViewListItemDto view, Dictionary<string, string> search, bool hasSearch, Users user, SqlCommand cmd, out string query, out string queryCount)
        {
            string connectionString = _commonServices.getConnectionString();

            // ---- Load fields for component (fast query)
            string sql = @"
            SELECT 
                Name, TableName, ColumnName, DataType,
                ReadOnly, Required, DisplayInForm, DisplayInList,
                IsCalc, CalcExpr, LookUp
            FROM ComponentField
            WHERE ComponentId = (SELECT Id FROM Component WHERE Name = @CompName)
            ORDER BY DisplaySequence";

            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@CompName", component.Name);
            
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
                string alias = f.FieldName;
                string col =
                    (f.IsCalc && !string.IsNullOrEmpty(f.CalcExpression))
                        ? "(" + ReplaceCalcDefaults(f.CalcExpression, user) + ")"
                        : $"{f.TableName}.{f.TableColumn}";

                if (f.DataType.Equals("LookUp", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(f.LookUp)
                    && f.LookUp != "-1")
                {
                    var lookupSql =
                        $"SELECT dbo.GetLookUpVal({f.LookUp}, '{user.Language}')";

                    var lookupExpression =
                        _commonServices.ExecuteQuery_OneValue(lookupSql, null, connectionString);

                    col = $"({lookupExpression}{col})";
                }

                else if (f.DataType.Equals("Date", StringComparison.OrdinalIgnoreCase))
                {
                    col = $"CONVERT(VARCHAR, {col}, 103)";
                }
                else if (f.DataType.Equals("DateTime", StringComparison.OrdinalIgnoreCase))
                {
                    col = $"FORMAT({col}, 'dd/MM/yyyy hh:mm tt')";
                }
                else if (f.DataType.Equals("Time", StringComparison.OrdinalIgnoreCase))
                {
                    col = $"FORMAT({col}, 'hh:mm tt')";
                }

                else if (f.DataType.Equals("CheckBox", StringComparison.OrdinalIgnoreCase))
                {
                    col = $"{col}";
                    alias = $"check_{f.FieldName}";
                }

                else if (f.DataType.Equals("Number", StringComparison.OrdinalIgnoreCase))
                {
                    col = $"{col}";
                }

                else if (f.DataType.Equals("Decimal", StringComparison.OrdinalIgnoreCase))
                {
                    col = $"FORMAT({col}, 'N3')";
                    alias = $"decimal_{f.FieldName}";
                }

                else if (f.DataType.Equals("Color", StringComparison.OrdinalIgnoreCase))
                {
                    col = $"{col}";
                    alias = $"color_{f.FieldName}";
                }

                else if (f.DataType.Equals("Button", StringComparison.OrdinalIgnoreCase) ||
                         f.DataType.Equals("InnerButton", StringComparison.OrdinalIgnoreCase))
                {
                    col = $"{col}";
                    alias = $"button_{f.FieldName}";
                }

                else if (f.DataType.Equals("File", StringComparison.OrdinalIgnoreCase))
                {
                    col = $"{col}";
                    alias = $"file_{f.FieldName}";
                }

                else
                {
                    col = $"{col}";
                }

                sbSelect.Append($"{col} {EscapeAlias(alias)},");
            }
            foreach (var f in fields)
            {
                if (f.FieldName.Equals(view.CompFieldName, StringComparison.OrdinalIgnoreCase))
                {
                    if (f.IsCalc)
                    {
                        view.CompFieldName = ReplaceCalcDefaults(f.CalcExpression, user);
                    }
                    else
                    {
                        view.CompFieldName = $"{f.TableName}.{f.TableColumn}";
                    }
                }

            }
            string selectColumns = sbSelect.ToString().TrimEnd(',');

            // ---- access filter
            string whereAccess = view.ViewDataAccess switch
            {
                1 => "",
                2 => $" GroupId IN (SELECT ID FROM dbo.GetUserGroups('{user.Id}')) ",
                _ => $" CreatedBy = '{user.Id}' "
            };
            string whereSearch = "";
            // ---- search conditions
            if (hasSearch)
            {
                whereSearch = BuildSearchConditions(search, fields, cmd);
            }
            

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
        private static string EscapeAlias(string alias)
        {
            // any SQL keyword you want to protect
            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Print", "Order", "Group", "User", "Key", "Type", "Index",
                "Table", "Select", "From", "To", "By", "In"
            };

            return reserved.Contains(alias) ? $"[{alias}]" : alias;
        }
        #endregion
        #region Export Excel Helper
        public void BuildExportExcelQuery(Component component, LoadViewListItemDto view, Users user, SqlCommand cmd, out string query)
        {
            string connectionString = _commonServices.getConnectionString();

            string sql = @"
            SELECT 
                Name, TableName, ColumnName, DataType,
                DisplayInList, IsCalc, CalcExpr, LookUp
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

            var sb = new StringBuilder();

            foreach (var f in fields.Where(f => f.DisplayInList))
            {
                string col =
                    (f.IsCalc && !string.IsNullOrEmpty(f.CalcExpression))
                        ? "(" + ReplaceCalcDefaults(f.CalcExpression, user) + ")"
                        : $"{f.TableName}.{f.TableColumn}";

                string alias = f.FieldName;

                // LOOKUP → text
                if (f.DataType.Equals("LookUp", StringComparison.OrdinalIgnoreCase)
                    && f.LookUp != "-1")
                {
                    string lookupSql =
                        $"SELECT dbo.GetLookUpVal({f.LookUp}, '{user.Language}')";

                    string lookupExpr = _commonServices.ExecuteQuery_OneValue(lookupSql, null, connectionString);
                    col = $"({lookupExpr}{col})";
                }

                // DATE
                if (f.DataType.Equals("Date", StringComparison.OrdinalIgnoreCase))
                    col = $"CONVERT(VARCHAR, {col}, 103)";

                // DATETIME
                else if (f.DataType.Equals("DateTime", StringComparison.OrdinalIgnoreCase))
                    col = $"FORMAT({col}, 'dd/MM/yyyy hh:mm tt')";

                // TIME
                else if (f.DataType.Equals("Time", StringComparison.OrdinalIgnoreCase))
                    col = $"FORMAT({col}, 'hh:mm tt')";

                // DECIMAL
                else if (f.DataType.Equals("Decimal", StringComparison.OrdinalIgnoreCase) )
                {
                    col = $"REPLACE(FORMAT({col}, 'N3'), ',', '')";
                }

                // COLOR
                else if (f.DataType.Equals("Color", StringComparison.OrdinalIgnoreCase))
                    continue;

                // BUTTON
                else if (f.DataType.Equals("Button", StringComparison.OrdinalIgnoreCase) ||
                         f.DataType.Equals("InnerButton", StringComparison.OrdinalIgnoreCase))
                    continue;

                // FILE
                else if (f.DataType.Equals("File", StringComparison.OrdinalIgnoreCase))
                    continue;

                sb.Append($"{col} [{alias}],");
            }

            string cols = sb.ToString().TrimEnd(',');

            string where = _commonServices.GetQueryWhere(
                view.CompFieldName,
                "",
                "",
                component.SearchSpec,
                user
            );

            query =
                $"SELECT {cols} FROM {component.TableName} " +
                $"{where} ORDER BY {component.TableName}.{component.SortSpec}";
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
