# KustoQueryAnalyzer

This tool can be used to analyze KQL queries and provide the following information:
* Query syntax errors.
* List of output columns and their types.
* List of referenced table names.
* List of referenced column names.
* List of referenced functions.

## Usage

The tool can be used in three different ways:

### 1. As a Docker container

To run the KQL Analyzer using Docker:

```bash
# Build and start the container
docker compose up -d

# The API will be available at http://localhost:8000
curl http://localhost:8000/api/environments

# Analyze a query
curl -X POST -H "Content-Type: application/json" -d@query.json http://localhost:8000/api/analyze

# Stop the container
docker compose down
```

### 2. As a command line tool

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

### 3. As a REST web service

The tool can also be used as a REST web service to analyze multiple queries without having to restart the executable each time.

```
dotnet run --project=src/KQLAnalyzer.csproj -- --rest --bind-address=http://localhost:8000
# The bind-address parameter is optional and defaults to http://localhost:8000.
```

The REST web service will be available at http://localhost:8000.

The following endpoints are available:
* POST /api/analyze - Analyze a query providing a query and platform.
* GET  /api/environments - List available platforms.

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

When both `sentinel` and `m365` environments have been defined, the tool will automatically create a merged environment called `m365_with_sentinel` which contains the tables and functions from both platforms. This allows analyzing queries targeting the merged Defender XDR platform.

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

For workspace functions whose output schema depends on their inputs, set
`output_type` to `"inferred"` and supply the KQL `parameters` and `query`:

```json
{
    "query": "print a=1 | invoke ExtendExample()",
    "environment": "sentinel",
    "local_data": {
        "tabular_functions": {
            "ExtendExample": {
                "output_type": "inferred",
                "parameters": "T:(*)",
                "query": "T | extend foo=\"bar\""
            }
        }
    }
}
```

This returns `a: long` and `foo: string`. The Kusto semantic analyzer infers the
schema at each call site using the actual input types; it does not execute the
query or fetch data. Function bodies can use KQL operators, `let` statements,
and other functions and tables supplied in the environment or `local_data`.
Inference is subject to the Kusto language library's support; schemas that
depend on data values cannot generally be determined without executing a query.
As with other database functions, the library does not report all errors inside
function bodies as diagnostics on the calling query; supply valid function definitions.

For inferred functions, `parameters` is the authoritative KQL declaration
(including table schemas and scalar defaults); `arguments` and `output_columns`
are not used. Outer parentheses around `parameters` and braces around `query`
are optional. Use `"parameters": ""` for functions without parameters. Both
`parameters` and a non-empty `query` are required. Existing functions using
`output_columns` continue to work as before.

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
