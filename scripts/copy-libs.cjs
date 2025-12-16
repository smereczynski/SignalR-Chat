#!/usr/bin/env node
const fs = require('fs');
const path = require('path');

function ensureDir(p){ if(!fs.existsSync(p)) fs.mkdirSync(p,{recursive:true}); }

const root = path.resolve(__dirname, '..');
const webrootLib = path.join(root, 'src','Chat.Web','wwwroot','lib');
ensureDir(webrootLib);

// Bootstrap copy (css + bundle js)
const bootstrapBase = path.join(root, 'node_modules','bootstrap','dist');
ensureDir(path.join(webrootLib,'bootstrap','css'));
ensureDir(path.join(webrootLib,'bootstrap','js'));
fs.copyFileSync(path.join(bootstrapBase,'css','bootstrap.min.css'), path.join(webrootLib,'bootstrap','css','bootstrap.min.css'));
fs.copyFileSync(path.join(bootstrapBase,'js','bootstrap.bundle.min.js'), path.join(webrootLib,'bootstrap','js','bootstrap.bundle.min.js'));

// SignalR copy (only browser dist)
const signalrBase = path.join(root,'node_modules','@microsoft','signalr','dist','browser');
ensureDir(path.join(webrootLib,'microsoft-signalr'));
for(const f of ['signalr.js','signalr.min.js']){
  fs.copyFileSync(path.join(signalrBase,f), path.join(webrootLib,'microsoft-signalr',f));
}

// Flag icons (CSS + a small subset of SVGs used by the app)
const flagIconsBase = path.join(root, 'node_modules', 'flag-icons');
const flagIconsCssDir = path.join(webrootLib, 'flag-icons', 'css');
const flagIconsFlags4x3Dir = path.join(webrootLib, 'flag-icons', 'flags', '4x3');
const flagIconsFlags1x1Dir = path.join(webrootLib, 'flag-icons', 'flags', '1x1');
ensureDir(flagIconsCssDir);
ensureDir(flagIconsFlags4x3Dir);
ensureDir(flagIconsFlags1x1Dir);

fs.copyFileSync(
  path.join(flagIconsBase, 'css', 'flag-icons.min.css'),
  path.join(flagIconsCssDir, 'flag-icons.min.css')
);

// Only copy flags we actually use.
// Include 'xx' as the placeholder used before JS sets the current culture.
const usedFlags = ['xx', 'gb', 'pl', 'de', 'cz', 'sk', 'ua', 'lt', 'ru'];
for (const code of usedFlags) {
  fs.copyFileSync(
    path.join(flagIconsBase, 'flags', '4x3', `${code}.svg`),
    path.join(flagIconsFlags4x3Dir, `${code}.svg`)
  );
  fs.copyFileSync(
    path.join(flagIconsBase, 'flags', '1x1', `${code}.svg`),
    path.join(flagIconsFlags1x1Dir, `${code}.svg`)
  );
}

console.log('Libraries copied to wwwroot/lib');
