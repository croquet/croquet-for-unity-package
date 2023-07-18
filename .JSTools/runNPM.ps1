# on Windows, the CroquetBuilder.StartBuild script supplies us with 2 arguments:
# 1. full path to the platform-relevant node engine
# 2. app name - used in webpack.config to find the app source
# 3. build target: 'node' or 'web'

$STDOUT=$args[0]
$STDERR=$args[1]

npm install 1>"$STDOUT" 2>"$STDERR"
