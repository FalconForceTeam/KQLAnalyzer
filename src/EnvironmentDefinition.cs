using System.Text.Json;
using System.Text.Json.Serialization;
using Kusto.Language;
using Kusto.Language.Symbols;

public class EnvironmentDefinition
{
    private static readonly FunctionSymbol FileProfileFunction =
        // Create a function that has the same return type as FileProfile
        // Copies all the columns from the existing table and adds the new ones as specified by Microsoft.
        // This is a workaround for the fact that the FileProfile function is not available in the Kusto API.
        // The join is there to get the same behavior as the FileProfile function where if a column
        // named GlobalPrevalence or any other output column is already present in the table,
        // it will not be overwritten but a new  column called GlobalPrevalence1 will be added.
        new(
            "FileProfile",
            "(T:(*), x:string='',y:string='')",
            """
            {
                (T | extend _TmpJoinKey=123)
                | join (
                    print SHA1='', SHA256='', MD5='', FileSize=0, GlobalPrevalence=0, GlobalFirstSeen=now(),
                    GlobalLastSeen=now(), Signer='', Issuer='', SignerHash='', IsCertificateValid=false,
                    IsRootSignerMicrosoft=false, SignatureState='', IsExecutable=false, ThreatName='',
                    Publisher='', SoftwareName='', ProfileAvailability='',_TmpJoinKey=123
                ) on _TmpJoinKey | project-away _TmpJoinKey, _TmpJoinKey1
            }
            """
        );

    private static readonly FunctionSymbol DeviceFromIPFunction =
        new(
            "DeviceFromIP",
            "(T:(*), x:string='',y:datetime='')",
            """
            {
                (T | extend _TmpJoinKey=123)
                | join (
                    print IP='', DeviceId='', _TmpJoinKey=123
                ) on _TmpJoinKey | project-away _TmpJoinKey, _TmpJoinKey1
            }
            """
        );

    public EnvironmentDefinition()
    {
        this.TableDetails = new Dictionary<string, TableDetails>();
        this.MagicFunctions = new List<string>();
    }

    [JsonPropertyName("tables")]
    public Dictionary<string, TableDetails> TableDetails { get; set; }

    [JsonPropertyName("magic_functions")]
    public List<string> MagicFunctions { get; set; }

    public GlobalState ToGlobalState()
    {
        List<Symbol> dbMembers = new List<Symbol>();

        foreach (var table in this.TableDetails)
        {
            List<ColumnSymbol> columns = table.Value
                .Select(column =>
                {
                    if (ScalarTypes.GetSymbol(column.Value) == null)
                    {
                        throw new Exception(
                            $"Unknown type {column.Value} for column {column.Key} in table {table.Key}"
                        );
                    }

                    return new ColumnSymbol(column.Key, ScalarTypes.GetSymbol(column.Value));
                })
                .ToList();
            dbMembers.Add(new TableSymbol(table.Key, columns));
        }

        if (this.MagicFunctions.Contains("FileProfile"))
        {
            dbMembers.Add(FileProfileFunction);
        }

        if (this.MagicFunctions.Contains("DeviceFromIP"))
        {
            dbMembers.Add(DeviceFromIPFunction);
        }

        return GlobalState.Default.WithDatabase(new DatabaseSymbol("db", dbMembers));
    }
}
