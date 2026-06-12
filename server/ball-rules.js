const circleMath = require("./circle-math");

function calculateBounceDirection(ballDir, defenderPaddleAngle, impactAngle, paddleArcDegrees, paddleAimInfluence) {
  const impactDirection = circleMath.angleToDirection(impactAngle);
  const inward = { x: -impactDirection.x, y: -impactDirection.y };

  const dot = ballDir.x * inward.x + ballDir.y * inward.y;
  let dirX = ballDir.x - 2 * dot * inward.x;
  let dirY = ballDir.y - 2 * dot * inward.y;

  const offset = circleMath.clamp(
    circleMath.deltaAngle(defenderPaddleAngle, impactAngle) / (paddleArcDegrees * 0.5),
    -1,
    1
  );
  const tangent = { x: -impactDirection.y, y: impactDirection.x };
  dirX += tangent.x * offset * paddleAimInfluence;
  dirY += tangent.y * offset * paddleAimInfluence;

  const inwardDot = dirX * inward.x + dirY * inward.y;
  if (inwardDot < 0.45) {
    dirX += inward.x * (0.45 - inwardDot);
    dirY += inward.y * (0.45 - inwardDot);
  }

  const length = Math.hypot(dirX, dirY) || 1;
  return { x: dirX / length, y: dirY / length };
}

function randomDirection() {
  const angle = Math.random() * 360;
  return circleMath.angleToDirection(angle);
}

module.exports = {
  calculateBounceDirection,
  randomDirection
};
