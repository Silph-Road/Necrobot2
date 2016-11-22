const config = require('electron-json-config')
 
function loadConfig() {
	alert("Config path loaded: " + config.get("ConsoleConfigPath"))
}

loadConfig()