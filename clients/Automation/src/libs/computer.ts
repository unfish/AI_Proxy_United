import { convertFileSrc, invoke } from "@tauri-apps/api/core";
import { appDataDir, join } from "@tauri-apps/api/path";

export type Monitor = {
  id: string;
  name: string;
  is_primary: boolean;
  width: number;
  height: number;
  x: number;
  y: number;
  scale_factor: number;
};

export async function getMonitors(): Promise<Array<Monitor>> {
  const result = await invoke<Array<Monitor>>("get_monitors");
  return result;
}

export async function takeScreenshot({
  monitorId,
  resizeX,
  resizeY,
  useCutMode = false,
  scaleFactor = 1.0,
}: {
  monitorId: string;
  resizeX: number;
  resizeY: number;
  useCutMode: boolean;
  scaleFactor: number;
}): Promise<string> {
  // wait 2 seconds to ensure the screen is ready
  await new Promise((resolve) => setTimeout(resolve, 1000));

  console.time("take_screenshot");
  const b64 = await invoke<string>("take_screenshot", {
    monitorId,
    resizeX,
    resizeY,
    useCutMode,
    scaleFactor
  });
  console.timeEnd("take_screenshot");

  return b64;
}

export async function moveMouse(monitorId: string, x: number, y: number) {
  // wait 2 seconds to ensure the screen is ready
  await new Promise((resolve) => setTimeout(resolve, 1000));

  await invoke("move_mouse", { monitorId, x, y });
}

export async function mouseClick(
  monitorId: string,
  side: "left" | "right",
  x?: number,
  y?: number
) {
  // wait 2 seconds to ensure the screen is ready
  await new Promise((resolve) => setTimeout(resolve, 1000));

  console.log("-- Mouse click:", { side, x, y });

  if (x === undefined && y === undefined) {
    await invoke("mouse_click", { monitorId, side });
  } else {
    await invoke("mouse_click", { monitorId, side, x, y });
  }
}

export async function mouseDoubleClick(
  monitorId: string,
  side: "left" | "right",
  x?: number,
  y?: number
) {
  // wait 2 seconds to ensure the screen is ready
  await new Promise((resolve) => setTimeout(resolve, 1000));

  console.log("-- Mouse click:", { side, x, y });

  if (x === undefined && y === undefined) {
    await invoke("mouse_double_click", { monitorId, side });
  } else {
    await invoke("mouse_double_click", { monitorId, side, x, y });
  }
}

export async function mouseDrag(
  monitorId: string,
  x: number,
  y: number
) {
  // wait 2 seconds to ensure the screen is ready
  await new Promise((resolve) => setTimeout(resolve, 1000));

  console.log("-- Mouse drag:", {  x, y });

  await invoke("mouse_drag", { monitorId, x, y });
}

export async function getCursorPosition(monitorId: string) {
  // wait 2 seconds to ensure the screen is ready
  await new Promise((resolve) => setTimeout(resolve, 1000));

  const result = await invoke<{ x: number; y: number }>("get_cursor_position", {
    monitorId,
  });
  return result;
}

export async function typeText(text: string) {
  // wait 2 seconds to ensure the screen is ready
  await new Promise((resolve) => setTimeout(resolve, 1000));

  await invoke("type_text", { text });
}

export async function pressKey(key: string) {
  // wait 2 seconds to ensure the screen is ready
  await new Promise((resolve) => setTimeout(resolve, 1000));
  await invoke("press_key", { key: key.toLowerCase() });
}

export async function justWait(duration: number) {
  // wait x seconds, do nothing
  await new Promise((resolve) => setTimeout(resolve, duration*1000));
}

export async function scrollMouse(amount: number, direction: string) {
  // wait 2 seconds to ensure the screen is ready
  await new Promise((resolve) => setTimeout(resolve, 1000));
  await invoke("mouse_scroll", { amount: amount, direction: direction.toLowerCase() });
}

export async function holdKey(key: string, duration: number) {
  // wait 2 seconds to ensure the screen is ready
  await new Promise((resolve) => setTimeout(resolve, 1000));
  await invoke("hold_key", { key: key.toLowerCase(), duration: duration });
}

export async function mouseDown() {
  // wait 2 seconds to ensure the screen is ready
  await new Promise((resolve) => setTimeout(resolve, 1000));
  await invoke("mouse_down");
}
export async function mouseUp() {
  // wait 2 seconds to ensure the screen is ready
  await new Promise((resolve) => setTimeout(resolve, 1000));
  await invoke("mouse_up");
}
