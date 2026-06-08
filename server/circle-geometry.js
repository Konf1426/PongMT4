function angleToDirection(angle) {
  const radians = angle * Math.PI / 180;
  return { x: Math.cos(radians), y: Math.sin(radians) };
}

function directionToAngle(x, y) {
  const angle = Math.atan2(y, x) * 180 / Math.PI;
  return angle < 0 ? angle + 360 : angle;
}

function angleInsideSector(angle, startAngle, endAngle) {
  const center = lerpAngle(startAngle, endAngle, 0.5);
  const halfSize = Math.abs(deltaAngle(startAngle, endAngle)) * 0.5;
  return Math.abs(deltaAngle(center, angle)) <= halfSize;
}

function clampPaddleAngle(angle, startAngle, endAngle, paddleArcDegrees) {
  const center = lerpAngle(startAngle, endAngle, 0.5);
  const sectorHalfSize = Math.abs(deltaAngle(startAngle, endAngle)) * 0.5;
  const allowedHalfSize = Math.max(0, sectorHalfSize - paddleArcDegrees * 0.5);
  const delta = clamp(deltaAngle(center, angle), -allowedHalfSize, allowedHalfSize);
  return center + delta;
}

function bounceDirection(ballDirX, ballDirY, defenderPaddleAngle, impactAngle, paddleArcDegrees, paddleAimInfluence) {
  const impactDirection = angleToDirection(impactAngle);
  const paddleDirection = angleToDirection(defenderPaddleAngle);
  const dot = ballDirX * paddleDirection.x + ballDirY * paddleDirection.y;
  let reflectedX = ballDirX - 2 * dot * paddleDirection.x;
  let reflectedY = ballDirY - 2 * dot * paddleDirection.y;

  const offset = deltaAngle(defenderPaddleAngle, impactAngle) / Math.max(1, paddleArcDegrees * 0.5);
  const tangent = { x: -paddleDirection.y, y: paddleDirection.x };
  let aimedX = reflectedX + tangent.x * offset * paddleAimInfluence;
  let aimedY = reflectedY + tangent.y * offset * paddleAimInfluence;
  let length = Math.hypot(aimedX, aimedY) || 1;
  aimedX /= length;
  aimedY /= length;

  const antiImpactDot = aimedX * -impactDirection.x + aimedY * -impactDirection.y;
  if (antiImpactDot < 0.15) {
    aimedX = lerp(aimedX, -impactDirection.x, 0.5);
    aimedY = lerp(aimedY, -impactDirection.y, 0.5);
    length = Math.hypot(aimedX, aimedY) || 1;
    aimedX /= length;
    aimedY /= length;
  }

  return { x: aimedX, y: aimedY };
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

module.exports = {
  angleInsideSector,
  angleToDirection,
  bounceDirection,
  clamp,
  clampPaddleAngle,
  deltaAngle,
  directionToAngle
};
