function angleToDirection(angle) {
  const radians = angle * Math.PI / 180;
  return { x: Math.cos(radians), y: Math.sin(radians) };
}

function directionToAngle(x, y) {
  const angle = Math.atan2(y, x) * 180 / Math.PI;
  return angle < 0 ? angle + 360 : angle;
}

function clampPaddleAngle(angle, startAngle, endAngle, paddleArcDegrees) {
  const center = lerpAngle(startAngle, endAngle, 0.5);
  const sectorHalfSize = Math.abs(deltaAngle(startAngle, endAngle)) * 0.5;
  const allowedHalfSize = Math.max(0, sectorHalfSize - paddleArcDegrees * 0.5);
  const delta = clamp(deltaAngle(center, angle), -allowedHalfSize, allowedHalfSize);
  return center + delta;
}

function angleInsideSector(angle, startAngle, endAngle) {
  const center = lerpAngle(startAngle, endAngle, 0.5);
  const halfSize = Math.abs(deltaAngle(startAngle, endAngle)) * 0.5;
  return Math.abs(deltaAngle(center, angle)) <= halfSize;
}

function lerpAngle(a, b, t) {
  return a + deltaAngle(a, b) * t;
}

function deltaAngle(current, target) {
  let delta = repeat((target - current), 360);
  if (delta > 180) {
    delta -= 360;
  }
  return delta;
}

function repeat(value, length) {
  return clamp(value - Math.floor(value / length) * length, 0, length);
}

function clamp(value, min, max) {
  return Math.min(max, Math.max(min, value));
}

function lerp(a, b, t) {
  return a + (b - a) * t;
}

function round(value) {
  return Math.round(value * 1000) / 1000;
}

module.exports = {
  angleInsideSector,
  angleToDirection,
  clamp,
  clampPaddleAngle,
  deltaAngle,
  directionToAngle,
  lerp,
  round
};
