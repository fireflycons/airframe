# Windows installer

* Create a directory within this project for the installer code
* Build a script for the NSIS Windows installer
* The NSIS suite is installed to `C:\Program Files (x86)\NSIS`
* Present a dialog to obtain defaults for observer location, radius and port (default 7700). Use NSCurl/NSJson to find the coordinates of the host's public IP and present as defaults, displaying city,region,country so the user knows what has been located. If there is an error performing location, leave input fields empty. Also provide a checkbox to select whether or not to install the screen saver
* Once the software is extracted to the installation location, configure the service to auto-start and start the service.
* If the user selected screen saver, install the screen saver and set it as the active saver for the current user.
* Request elevation as needed.
* Modify `Makafile` to include NSIS targets if the OS is Windows
