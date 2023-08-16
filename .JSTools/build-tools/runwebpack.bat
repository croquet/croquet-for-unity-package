:: on Windows, the CroquetBuilder.StartBuild script supplies us with 2 arguments:
:: 1. full path to the platform-relevant node engine
:: 2. app name - used in webpack.config to find the app source
:: 3. build target: 'node' or 'web'

@echo off
set nodepath=%1
set appname=%2
set target=%3

%nodepath% ..\node_modules\webpack\bin\webpack.js --config webpack.config.js --mode development --env appName=%appname% --env buildTarget=%target% --no-color

echo webpack-exit=%errorlevel%
