"""Explicit PBR source authoring entry point. Never runs automatically in Unity."""
from pathlib import Path
import runpy
runpy.run_path(str(Path(__file__).with_name('rebuild_city_hatch_surfaces.py')), run_name='__main__')
