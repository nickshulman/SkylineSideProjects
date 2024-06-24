pushd v23_1
k:ResourcesOrganizer add --db ..\v23_1.db pwiz_tools
popd
pushd v24_1
k:ResourcesOrganizer add --db ..\v24_1.db pwiz_tools
popd
copy v24_1.db newstrings.db
k:ResourcesOrganizer subtract --db newstrings.db v23_1.db
k:ResourcesOrganizer export --db newstrings.db newstrings.zip
(
echo .mode tabs
echo .header on
echo .output newstrings.txt
type exportstrings.sql
) | sqlite3.exe newstrings.db
