# KustoQueryAnalyzer

This tool can be used to analyze KQL queries and provide the following information:
* Query syntax errors.
* List of output columns and their types.
* List of referenced table names.
* List of referenced column names.
* List of referenced functions.

## Usage

The tool can be used in two different ways:

### 1. As a command line tool

The tool can be used as a command line tool to analyze a single query.

```
dotnet run --project=src/KQLAnalyzer.csproj -- --input-file <input-file>
```

For example:
```
dotnet run --project=src/KQLAnalyzer.csproj -- --input-file=query.json
```

Where query.json is a file containing the query to be analyzed in JSON format, for example:
```
{
    "query": "let RuleId=12345;SigninLogs|where UserPrincipalName in~ ((_GetWatchlist(strcat('wl','_',RuleId)) | project SPN)) | project TimeGenerated, UserPrincipalName",
    "environment": "sentinel",
    "local_data": {
        "watchlists": {
            "wl_12345": {
                "SPN": "string"
            }
        }
    }
}
```

For this example the following output is given by the tool:
```
{
  "output_columns": {
    "TimeGenerated": "datetime",
    "UserPrincipalName": "string"
  },
  "parsing_errors": [],
  "referenced_tables": [
    "SigninLogs"
  ],
  "referenced_functions": [
    "_GetWatchlist"
  ],
  "referenced_columns": [
    "UserPrincipalName",
    "TimeGenerated"
  ],
  "elapsed_ms": 94
}
```

### 2. As a REST web service

The tool can also be used as a REST web service to analyze multiple queries without having to restart the executable each time.

```
dotnet run --project=src/KQLAnalyzer.csproj -- --rest --bind-address=http://localhost:8000
# The bind-address parameter is optional and defaults to http://localhost:8000.
```

The REST web service will be available at http://localhost:8000.

The following endpoints are available:
* POST /api/analyze - Analyze a query providing a query and platform.
* GET  /api/platforms - List available platforms.

Example usage:
```
curl -X POST -H "Content-Type: application/json" -d@query.json http://localhost:8000/api/analyze
```

The input format for the POST request is the same as the input format for the command line tool.

## Getting Schema Information

The tool can parse Microsoft documentation to get schema information for the Sentinel and M365 Defender platforms.

To update the the schema information based on the latest documentation, run the following command:
```
./update_schemas.sh
```

This will produce a file called `environments.json` which contains details about the tables and built-in functions for each platform.

A file called `environments.json` is already included in the repository, so you don't need to run this command unless you want to update the schema information.

## Input file format

The query.json file can contain the following properties:
* `environment` - The platform to use for the query. This can be one of the following values: `sentinel` or `m365`.
* `query` - The query to analyze.
* `local_data` - A dictionary containing local data that can be used to analyze the query. This is useful if you want to analyze a query that uses custom tables or functions. The format of this property is described below.

The `local_data` property can contain the following properties:
* `tables` - A list of tables and their corresponding columns that are present in the environment. This is useful if you want to analyze a query that uses custom tables. The format of this property is the same as the `tables` property in the `environments.json` file.
* `scalar_functions` - A list of scalar functions that are present in the environment. A scalar function is a function that returns a single value.
* `tabular_functions` - A list of tabular functions that are present in the environment. A tabular function is a function that returns a table.
* `watchlists` - A list of watchlists that are present in the environment and their corresponding custom output columns.

A more complex example that provides all of these is given below:
```
{
    "query": "print a=MyScalar('foo')",
    "environment": "sentinel",
    "local_data": {
        "tables": {
            "MyTable": {
                "Timestamp": "datetime",
                "Value": "string"
            }
        },
        "scalar_functions": {
            "MyScalar": {
                "output_type": "bool",
                "arguments": [
                    {
                        "name": "foo",
                        "type": "string",
                        "optional": true
                    }
                ]
            }
        },
        "tabular_functions": {
            "MyFunction": {
                "output_columns": {
                    "output_foo": "string"
                },
                "arguments": [
                    {
                        "name": "foo",
                        "type": "string",
                        "optional": true
                    }
                ]
            }
        },
        "watchlists": {
            "example": {
                "foo": "string"
            }
        }
    }
}
```