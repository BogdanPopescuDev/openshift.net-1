@echo off

set mssql_url=mssql://%OPENSHIFT_MSSQL2012_DB_USERNAME%:%OPENSHIFT_MSSQL2012_DB_PASSWORD%@%OPENSHIFT_GEAR_DNS%:%OPENSHIFT_MSSQL2012_DB_PROXY_PORT%/

echo OPENSHIFT_MSSQL2012_DB_GEAR_UUID=%OPENSHIFT_GEAR_UUID%
echo OPENSHIFT_MSSQL2012_DB_GEAR_DNS=%OPENSHIFT_GEAR_DNS%
echo OPENSHIFT_MSSQL2012_DB_USERNAME=%OPENSHIFT_MSSQL2012_DB_USERNAME%
echo OPENSHIFT_MSSQL2012_DB_PASSWORD=%OPENSHIFT_MSSQL2012_DB_PASSWORD%
echo OPENSHIFT_MSSQL2012_DB_HOST=%OPENSHIFT_GEAR_DNS%
echo OPENSHIFT_MSSQL2012_DB_PORT=%OPENSHIFT_MSSQL2012_DB_PROXY_PORT%
echo OPENSHIFT_MSSQL2012_DB_URL=%mssql_url%