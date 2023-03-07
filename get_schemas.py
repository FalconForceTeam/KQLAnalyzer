import glob
import json
import os
import textwrap

# Extract tables from markdown files in Microsoft documentation.
def get_table_details(fn):
    inside_table = False
    table_name = None
    details = {}
    for line in open(fn):
        line = line.strip()
        if not line:
            continue
        line = line.replace('`','')
        if not table_name and line.startswith('# '):
            table_name = line.split()[-1]
        if (
            line.lower().startswith('## columns')
            or line.lower().startswith('| column name')
            or line.lower().startswith('|column name')
            ):
            inside_table = True
            continue
        # if not line.startswith('|'):
        #     inside_table = False
        if not inside_table or not line.startswith('|'):
            continue
        column_details = line.replace(' ','').split('|')
        if len(column_details) < 4:
            continue
        column_name = column_details[1]
        column_type = column_details[2].lower()
        if column_type == 'bigint':
            column_type = 'long'
        if column_name == 'Column' or column_name.startswith('--') or not column_name:
            continue
        details[column_name] = column_type
    return table_name, details

def merge_additional_columns(tables, env_name):
    additional_columns = json.load(open('additional_columns.json'))[env_name]
    for table_name, extra_fields in additional_columns.items():
        if table_name not in tables:
            tables[table_name] = {}
        for field_name, field_type in extra_fields.items():
            tables[table_name][field_name] = field_type

environments = {
    'm365': {
         'dir_name': 'defender_docs',
         'glob': '*-table.md',
         'help': textwrap.dedent("""
            git clone https://github.com/MicrosoftDocs/microsoft-365-docs
            mv microsoft-365-docs/microsoft-365/security/defender defender_docs
            rm -Rf microsoft-365-docs # optional to save disk space
        """),
        'magic_functions': [
            'FileProfile',
            'DeviceFromIP'
        ]
    },
    'sentinel': {
       'dir_name': 'sentinel_docs',
         'glob': '*.md',
       'help': textwrap.dedent("""
            git clone https://github.com/MicrosoftDocs/azure-reference-other
            mv azure-reference-other/azure-monitor-ref/tables sentinel_docs
            rm -Rf azure-reference-other # optional to save disk space
        """),
    }
}

def main():
    environment_details = {}
    for env_name, env_details in environments.items():
        if not os.path.exists(env_details['dir_name']):
            print(f"ERROR: {env_details['dir_name']} does not exist. To create it, run:\n{env_details['help'].strip()}")
            exit(1)
        tables = {}
        glob_pattern = os.path.join(env_details['dir_name'], env_details['glob'])
        for table_fn in sorted(glob.glob(glob_pattern)):
            table_name, details = get_table_details(table_fn)
            tables[table_name] = details
        merge_additional_columns(tables, env_name)
        details = dict(tables=tables, magic_functions=env_details.get('magic_functions', []))
        environment_details[env_name] = details
    print(json.dumps(environment_details, indent=2))

if __name__ == '__main__':
    main()
