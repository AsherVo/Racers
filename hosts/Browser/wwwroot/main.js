import { dotnet } from './_framework/dotnet.js';

const status = document.getElementById('status');
const canvas = document.getElementById('canvas');

// Copies the files listed in Content/manifest.json into the Emscripten file system at /Content,
// where the engine's ContentManager reads them through SDL like on every other platform.
async function loadContent(Module) {
    const files = await (await fetch('./Content/manifest.json')).json();
    await Promise.all(files.map(async path => {
        const response = await fetch(`./Content/${path}`);
        if (!response.ok) {
            throw new Error(`Failed to load Content/${path}: ${response.status}`);
        }
        const full = `/Content/${path}`;
        const dir = full.slice(0, full.lastIndexOf('/'));
        Module.FS_createPath('/', dir.slice(1), true, true);
        Module.FS_createDataFile(dir, full.slice(dir.length + 1), new Uint8Array(await response.arrayBuffer()), true, false, false);
    }));
}

try {
    const { Module, getAssemblyExports, getConfig } = await dotnet
        // SDL's Emscripten backend renders into Module.canvas.
        .withModuleConfig({ canvas })
        .create();

    await loadContent(Module);

    const exports = await getAssemblyExports(getConfig().mainAssemblyName);
    const game = exports.Browser.Program;

    if (!game.Init()) {
        throw new Error('Game failed to initialize; see the console for SDL errors.');
    }
    status.remove();

    const frame = () => {
        if (game.Frame()) {
            requestAnimationFrame(frame);
        }
    };
    requestAnimationFrame(frame);
} catch (err) {
    status.textContent = String(err);
    status.classList.add('error');
    throw err;
}
