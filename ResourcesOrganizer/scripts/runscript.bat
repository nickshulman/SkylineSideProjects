setlocal
set ResourcesOrganizerExe=%~dp0..\ResourcesOrganizer\bin\Release\net8.0\ResourcesOrganizer.exe
REM Syntax used in this batch file:
REM "~dp0": directory containing the executing batch file
pushd v23_1
%ResourcesOrganizerExe% add --db ..\v23_1.db pwiz_tools
popd
pushd v24_1
%ResourcesOrganizerExe% add --db ..\v24_1.db pwiz_tools
popd
copy v24_1.db newstrings.db
%ResourcesOrganizerExe% subtract --db newstrings.db v23_1.db
%ResourcesOrganizerExe% export --db newstrings.db newstrings.zip
(
echo .mode csv
echo .header on
echo .output newstrings.csv
type exportstrings.sql
) | sqlite3.exe newstrings.db
