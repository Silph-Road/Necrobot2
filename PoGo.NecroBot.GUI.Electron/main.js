const electron = require('electron')
// Module to control application life.
const app = electron.app
// Module to create native browser window.
const BrowserWindow = electron.BrowserWindow
const protocol = electron.protocol
const Menu = electron.Menu;

const config = require('electron-json-config');
const path = require('path')
const url = require('url')

// Keep a global reference of the window object, if you don't, the window will
// be closed automatically when the JavaScript object is garbage collected.
let mainWindow
let captchaWindow
let optionsWindow

function showCaptchaWindow(captchaUrl) {
  captchaWindow = new BrowserWindow({
    width: 600, 
	height: 500, 
	show: false,
	webPreferences: {
      nodeIntegration: true,
      webSecurity: true,
      preload: path.resolve(path.join(__dirname, 'scripts/preload.js'))
    }
  })
  
  captchaWindow.loadURL(captchaUrl)
  captchaWindow.once('ready-to-show', () => {
	captchaWindow.show()
  })
}

function showOptionsWindow() {
  optionsWindow = new BrowserWindow({
    width: 600, 
	height: 500, 
	show: false,
	webPreferences: {
      nodeIntegration: true,
      webSecurity: true,
      preload: path.resolve(path.join(__dirname, 'scripts/preload.js'))
    }
  })
  
  optionsWindow.loadURL(url.format({
    pathname: path.join(__dirname, 'options.html'),
    protocol: 'file:',
    slashes: true
  }))
  
  optionsWindow.once('ready-to-show', () => {
	optionsWindow.show()
  })
}

function createWindow () {
  // Create the browser window.
  mainWindow = new BrowserWindow({
	  width: 800, 
	  height: 600,
	  show: false,
	  webPreferences: {
        nodeIntegration: true,
        webSecurity: true,
        preload: path.resolve(path.join(__dirname, 'scripts/preload.js'))
      }
  })

  // and load the index.html of the app.
  mainWindow.loadURL(url.format({
    pathname: path.join(__dirname, 'PokeEase-Necrobot-Private/index.html'),
    protocol: 'file:',
    slashes: true
  }))

  // Open the DevTools.
  //mainWindow.webContents.openDevTools()

  // Emitted when the window is closed.
  mainWindow.on('closed', function () {
    // Dereference the window object, usually you would store windows
    // in an array if your app supports multi windows, this is the time
    // when you should delete the corresponding element.
    mainWindow = null
  })

  mainWindow.once('ready-to-show', () => {
	mainWindow.show()
  })
  
  protocol.registerFileProtocol('unity', (request, callback) => {
    const captchaToken = request.url.substr(6)
	// TODO Send back to CLI.
	console.log('Captcha token:');
	console.log(captchaToken);
  }, (error) => {
    if (error) {
	  console.error('Failed to register unity protocol')
	}
  })
  
  initializeConfig();
}

function initializeConfig() {
    console.log("Config file: " + config.file())
	if (config.get("ConsoleConfigPath") == null) {
	  config.set("ConsoleConfigPath", path.join(path.join(__dirname, '..'), 'Config'))
	}
}

// This method will be called when Electron has finished
// initialization and is ready to create browser windows.
// Some APIs can only be used after this event occurs.
app.on('ready', createWindow)

// Quit when all windows are closed.
app.on('window-all-closed', function () {
  // On OS X it is common for applications and their menu bar
  // to stay active until the user quits explicitly with Cmd + Q
  if (process.platform !== 'darwin') {
    app.quit()
  }
})

app.on('activate', function () {
  // On OS X it's common to re-create a window in the app when the
  // dock icon is clicked and there are no other windows open.
  if (mainWindow === null) {
    createWindow()
  }
})

const menu = Menu.buildFromTemplate([
  {
    label: 'File',
    submenu: [
      {
        label: 'Options',
        click: function() {
          showOptionsWindow();
        }
      },
	  {
			type: 'separator'
	  },
	  {
		label: 'Quit',
		accelerator: 'CmdOrCtrl+Q',
		click: () => {
			app.quit();
		}
	  }
    ]
  }
]);

Menu.setApplicationMenu(menu);


// In this file you can include the rest of your app's specific main process
// code. You can also put them in separate files and require them here.
