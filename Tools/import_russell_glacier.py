#!/usr/bin/env python3
"""Reproduce the Russell Glacier crop from PANGAEA 955957.

Requires numpy, scipy, rasterio, Pillow, tifffile and imagecodecs.
Run from any directory. Original rasters are cached outside Unity's Assets folder.
The orthomosaic cache is deliberately sparse: only required scanlines are fetched.
"""
import argparse
import concurrent.futures
import json
import os
from pathlib import Path
import time
import urllib.request

import numpy as np
from PIL import Image
import rasterio
from rasterio.windows import Window
from scipy.ndimage import gaussian_filter, uniform_filter
import tifffile

BASE = 'https://download.pangaea.de/dataset/955957/files/'
DEM = 'Wingtra_flight_12_highres_dsm_10cm.tif'
ORTHO = 'Wingtra_flight_12_highres_orthomosaic.tif'
PROJECT = Path(__file__).resolve().parents[1]


def remote_size(name):
    with urllib.request.urlopen(urllib.request.Request(BASE + name, method='HEAD'), timeout=30) as response:
        return int(response.headers['Content-Length'])


def fetch(name, path, start, end):
    """Cache only verified HTTP ranges; file size alone cannot validate a sparse TIFF."""
    marker = path.parent / (path.name + '.ranges') / f'{start}-{end}'
    if marker.exists() and path.exists():
        return
    for attempt in range(5):
        try:
            request = urllib.request.Request(BASE + name, headers={'Range': f'bytes={start}-{end-1}'})
            with urllib.request.urlopen(request, timeout=45) as response:
                if response.status != 206:
                    raise RuntimeError('Server did not honor the byte range')
                data = response.read()
            if len(data) != end-start:
                raise RuntimeError('Incomplete HTTP range')
            with path.open('r+b') as stream:
                stream.seek(start)
                stream.write(data)
            marker.parent.mkdir(exist_ok=True)
            marker.touch()
            return
        except Exception:
            if attempt == 4:
                raise
            time.sleep(attempt+1)


def fetch_many(name, path, intervals):
    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
        futures = [pool.submit(fetch, name, path, start, end) for start, end in intervals]
        for index, future in enumerate(concurrent.futures.as_completed(futures)):
            future.result()
            if index % 100 == 0:
                print(f'{name}: {index+1}/{len(futures)} ranges', flush=True)


def chunks(start, end):
    return [(i, min(i+8*1024**2, end)) for i in range(start, end, 8*1024**2)]


def export_heights(dem_path, destination, col, row):
    with rasterio.open(dem_path) as source:
        if source.crs.to_epsg() != 32622 or not np.allclose(source.res, (.1, .1)):
            raise ValueError('Expected the 10 cm UTM 22N survey raster')
        a = source.read(1, window=Window(col-1024, row-1024, 2049, 2049))
        if a.shape != (2049, 2049) or not np.all(np.isfinite(a) & (a != source.nodata)):
            raise ValueError('Driving crop contains missing survey data; choose another center')
        dy, dx = np.gradient(gaussian_filter(a, 3), .1)
        slope = np.degrees(np.arctan(np.hypot(dx, dy)))
        score = uniform_filter(slope, size=81) + uniform_filter(slope**2, size=81)**.5
        score[:350] = score[-350:] = 999
        score[:, :350] = score[:, -350:] = 999
        sy, sx = np.unravel_index(score.argmin(), score.shape)
        center = source.xy(row, col)
        low, high = float(np.floor(a.min())-2), float(np.ceil(a.max())+2)
        np.round((np.flipud(a)-low)/(high-low)*65535).astype('<u2').tofile(destination/'heights.bytes')
        metadata = dict(resolution=2049, size=204.8, baseElevation=low, heightRange=high-low,
                        centerEasting=center[0], centerNorthing=center[1], spawnX=float(sx*.1),
                        spawnZ=float((2048-sy)*.1), spawnElevation=float(a[sy, sx]-low),
                        spawnSlopeDegrees=float(slope[sy, sx]), crs='EPSG:32622',
                        heightSource=DEM, imageSource=ORTHO)
        return metadata


def export_images(path, destination, metadata):
    # TIFF directory and strip arrays are in the file tail; the header points to them.
    size = remote_size(ORTHO)
    if not path.exists():
        with path.open('wb') as stream:
            stream.truncate(size)
    fetch_many(ORTHO, path, [(0, 1024), (size-4*1024**2, size)])
    with tifffile.TiffFile(path) as tiff:
        page = tiff.pages[0]
        offsets, counts = page.dataoffsets, page.databytecounts
        if page.rowsperstrip != 1:
            raise ValueError('Expected one scanline per strip; source layout changed')
    with rasterio.open(path) as source:
        transform = source.transform
    def indices(extent, count):
        left = metadata['centerEasting']-extent/2
        top = metadata['centerNorthing']+extent/2
        xs = np.floor((left+(np.arange(count)+.5)*extent/count-transform.c)/transform.a).astype(int)
        ys = np.floor((transform.f-top+(np.arange(count)+.5)*extent/count)/-transform.e).astype(int)
        return xs, ys
    xs, ys = indices(metadata['size'], 2048)
    start, end = offsets[ys.min()], offsets[ys.max()]+counts[ys.max()]
    fetch_many(ORTHO, path, chunks(start, end))
    with tifffile.TiffFile(path) as tiff, path.open('rb') as stream:
        page = tiff.pages[0]
        image = np.empty((len(ys), len(xs), 3), dtype=np.uint8)
        for i, y in enumerate(ys):
            stream.seek(offsets[y])
            row = page.decode(stream.read(counts[y]), int(y))[0].reshape((-1, 4))
            image[i] = row[xs, :3]
        Image.fromarray(image).save(destination/'color.png')


def select_ice_spawn(destination, metadata):
    # Exclude dark pools and shadowed margins; choose a gently sloped bright surface.
    n = metadata['resolution']
    heights = np.flipud(np.fromfile(destination/'heights.bytes', dtype='<u2').reshape(n, n)).astype(float)
    heights = heights / 65535 * metadata['heightRange']
    image = np.asarray(Image.open(destination/'color.png').resize((n, n)), dtype=float)
    brightness = uniform_filter(image.mean(axis=2), size=51)
    dy, dx = np.gradient(gaussian_filter(heights, 3), .1)
    slope = np.degrees(np.arctan(np.hypot(dx, dy)))
    score = uniform_filter(slope, size=31) + uniform_filter(slope**2, size=31)**.5 + slope
    score[brightness < 180] = 999
    score[:250] = score[-250:] = 999
    score[:, :250] = score[:, -250:] = 999
    sy, sx = np.unravel_index(score.argmin(), score.shape)
    if score[sy, sx] >= 999:
        raise ValueError('No bright, valid starting surface found')
    metadata.update(spawnX=float(sx*.1), spawnZ=float((n-1-sy)*.1),
                    spawnElevation=float(heights[sy, sx]), spawnSlopeDegrees=float(slope[sy, sx]))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cache', type=Path, default=PROJECT/'.terrain-cache')
    parser.add_argument('--dem', type=Path, help='Use an already downloaded complete source DEM')
    parser.add_argument('--column', type=int, default=21360)
    parser.add_argument('--row', type=int, default=13468)
    parser.add_argument('--output', type=Path, default=PROJECT/'Assets/Terrain/Data')
    args = parser.parse_args()
    args.cache.mkdir(parents=True, exist_ok=True)
    args.output.mkdir(parents=True, exist_ok=True)
    dem = args.dem or args.cache/DEM
    if args.dem is None:
        size = remote_size(DEM)
        dem.touch(exist_ok=True)
        fetch_many(DEM, dem, chunks(0, size))
    metadata = export_heights(dem, args.output, args.column, args.row)
    export_images(args.cache/ORTHO, args.output, metadata)
    select_ice_spawn(args.output, metadata)
    (args.output/'terrain.json').write_text(json.dumps(metadata, indent=2)+'\n')
    print('Imported real-scale terrain and aligned imagery. Run GlacierSceneBuilder.Build in Unity.')


if __name__ == '__main__':
    main()
