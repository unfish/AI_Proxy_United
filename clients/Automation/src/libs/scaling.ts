// sizes above XGA/WXGA are not recommended (see README.md)
// scale down to one of these targets if ComputerTool._scaling_enabled is set

export type Resolution = {
  width: number;
  height: number;
};

const MAX_SCALING_TARGETS: Record<string, Resolution> = {
  XGA: { width: 1024, height: 768 }, // 4:3
  WXGA: { width: 1280, height: 800 }, // 16:10
  FWXGA: { width: 1366, height: 768 }, // ~16:9
};

export enum ScalingSource {
  COMPUTER = "computer",
  API = "api",
}

class ToolError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "ToolError";
  }
}

/**
 * Scales coordinates between the actual screen resolution and standardized target resolutions
 * to ensure consistent behavior across different screen sizes.
 *
 * When source is COMPUTER:
 * - Scales down coordinates from actual screen size to closest matching target resolution
 * - Used when receiving coordinates from mouse/screen events
 *
 * When source is API:
 * - Scales up coordinates from target resolution to actual screen size
 * - Used when receiving coordinates from API/tool calls
 *
 * @param screenDimensions The screen dimensions {width, height} to scale from/to
 * @param source Whether coordinates are coming from computer events or API calls
 * @param x The x coordinate to scale
 * @param y The y coordinate to scale
 * @param useCutMode use CutMode instead of ScaleMode for screenshot, no scaling for coordinates
 * @returns Tuple of scaled [x, y] coordinates
 * @throws ToolError if API coordinates are out of bounds
 */
export function scaleCoordinates({
  source,
  screenDimensions,
  x,
  y,
  useCutMode = false,
}: {
  source: ScalingSource;
  screenDimensions: { width: number; height: number };
  x: number;
  y: number;
  useCutMode: boolean;
}): [number, number] {
  //使用裁剪模式时默认裁剪1280*800部分
  if (useCutMode) {
    let target = Object.values(MAX_SCALING_TARGETS)[1];
    return source===ScalingSource.COMPUTER? [target.width, target.height] : [x, y];
  }
  // Calculate aspect ratio of current screen
  const ratio = screenDimensions.width / screenDimensions.height;

  // Find closest matching target resolution
  let closestDimension = Object.values(MAX_SCALING_TARGETS)[0];
  let smallestDiff = Math.abs(
    ratio - closestDimension.width / closestDimension.height
  );

  for (const dimension of Object.values(MAX_SCALING_TARGETS)) {
    const dimensionRatio = dimension.width / dimension.height;
    const diff = Math.abs(dimensionRatio - ratio);
    if (diff < smallestDiff) {
      closestDimension = dimension;
      smallestDiff = diff;
    }
  }

  const xScalingFactor = closestDimension.width / screenDimensions.width;
  const yScalingFactor = closestDimension.height / screenDimensions.height;

  //console.log("✅ closestDimension", closestDimension);
  //console.log("✅ xScalingFactor", xScalingFactor);
  //console.log("✅ yScalingFactor", yScalingFactor);

  if (source === ScalingSource.API) {
    // Scale up from target resolution to actual screen size
    return [Math.round(x / xScalingFactor), Math.round(y / yScalingFactor)];
  }

  // Scale down from actual screen size to target resolution
  return [Math.round(x * xScalingFactor), Math.round(y * yScalingFactor)];
}
