"""Parse a generated desktop entry with GLib without launching its executable."""
import sys
import gi
from gi.repository import GLib

try:
    gi.require_version("GioUnix", "2.0")
    from gi.repository import GioUnix as desktop_api
except ValueError:
    from gi.repository import Gio as desktop_api

entry = desktop_api.DesktopAppInfo.new_from_filename(sys.argv[1])
if entry is None:
    raise RuntimeError("GLib refused the desktop entry")
# get_executable() is a diagnostic first-word label, not the launch argv parser.
command = entry.get_commandline()
expanded = []
offset = 0
while offset < len(command):
    if command[offset] == "%":
        if offset + 1 >= len(command) or command[offset + 1] != "%":
            raise RuntimeError("Unexpected desktop field code")
        expanded.append("%")
        offset += 2
    else:
        expanded.append(command[offset])
        offset += 1
success, arguments = GLib.shell_parse_argv("".join(expanded))
if not success or arguments != [sys.argv[2]] or entry.get_name() != "Treesi Preview":
    raise RuntimeError(f"GLib interpreted unexpected launch arguments: {arguments!r}")
if entry.get_boolean("Terminal") or entry.get_boolean("StartupNotify"):
    raise RuntimeError("Unexpected desktop activation flags")
if entry.get_string("Icon") != sys.argv[3] or entry.get_icon().get_file().get_path() != sys.argv[3]:
    raise RuntimeError("GLib interpreted an unexpected desktop icon path")
print("GLib desktop entry parsing passed; no application was launched.")
