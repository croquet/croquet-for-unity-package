
set nodepath=%1
set appname=%2
set target=%3

%nodepath% ..\node_modules\webpack\bin\webpack.js --config webpack.config.js --mode development --env appName=%appname% --env buildTarget=%target% --no-color

echo webpack-exit=%errorlevel%
