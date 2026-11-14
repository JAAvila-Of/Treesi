"""Parse a generated desktop entry with GLib without launching its executable."""
import sys
import gi

try:
    gi.require_version("GioUnix", "2.0")
    from gi.repository import GioUnix as desktop_api
except ValueError:
    from gi.repository import Gio as desktop_api

entry = desktop_api.DesktopAppInfo.new_from_filename(sys.argv[1])
if entry is None:
    raise RuntimeError("GLib refused the desktop entry")
if entry.get_executable() != sys.argv[2] or entry.get_name() != "Treesi Preview":
    raise RuntimeError("GLib interpreted a different desktop executable or name")
if entry.get_boolean("Terminal") or entry.get_boolean("StartupNotify"):
    raise RuntimeError("Unexpected desktop activation flags")
print("GLib desktop entry parsing passed; no application was launched.")
