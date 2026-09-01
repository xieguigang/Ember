@echo off

set host="bin\Ember.exe"

%host% --http -p 80 --wwwroot="./web"

pause 