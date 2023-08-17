// invoked with an env.appName argument, which is used to determine the source
// and the output directories, and env.buildTarget which is either "node" or "web".
// NB: even if invoked from a different working directory, __dirname is the
// location of this config
const CopyPlugin = require('copy-webpack-plugin');
const HtmlWebPackPlugin = require('html-webpack-plugin');
const path = require('path');

module.exports = env => ({
    entry: () => {
        // if this is a node build, look for index-node.js in the app directory
        if (env.buildTarget === 'node') {
            try {
                const index = `../../${env.appName}/index-node.js`;
                require.resolve(index);
                return index;
            } catch (e) {/* fall through and try index.js next */}
        }
        // otherwise (or if index-node not found) assume there is an index.js
        const index = `../../${env.appName}/index.js`;
        require.resolve(index);
        return index;
    },
    output: {
        path: path.join(__dirname, `../../../StreamingAssets/${env.appName}/`),
        pathinfo: false,
        filename: env.buildTarget === 'node' ? 'node-main.js' : 'index-[contenthash:8].js',
        chunkFilename: 'chunk-[contenthash:8].js',
        clean: true
    },
    cache: {
        type: 'filesystem',
        name: `${env.appName}-${env.buildTarget}`,
        buildDependencies: {
            config: [__filename],
        }
    },
    resolve: {
        modules: [path.resolve(__dirname, '../node_modules')],
        alias: {
            '@croquet/game-models$': path.resolve(__dirname, 'sources/game-support-models.js'),
            '@croquet/unity-bridge$': path.resolve(__dirname, 'sources/unity-bridge.js'),
        },
        fallback: { "crypto": false }
    },
    experiments: {
        asyncWebAssembly: true,
    },
    module: {
        rules: [
            {
                test: /\.js$/,
                enforce: "pre",
                use: ["source-map-loader"],
            },
        ],
    },
    plugins: [
        new CopyPlugin({
            patterns: [
                { from: `../../${env.appName}/scene-definitions.txt`, to: 'scene-definitions.txt', noErrorOnMissing: true }
            ]
        })].concat(env.buildTarget === 'node' ? [] : [
            new HtmlWebPackPlugin({
                template: './sources/index.html',   // input
                filename: 'index.html',   // output filename in build
            }),
        ]),
    externals: env.buildTarget !== 'node' ? [] : [
        {
            'utf-8-validate': 'commonjs utf-8-validate',
            bufferutil: 'commonjs bufferutil',
        },
    ],
    target: env.buildTarget || 'web'
});
