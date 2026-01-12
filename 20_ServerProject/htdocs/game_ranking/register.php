<?php
require_once 'config.php';

// POSTデータの取得
$input = json_decode(file_get_contents('php://input'), true);

if (!isset($input['clear_time']) || !is_numeric($input['clear_time'])) {
    http_response_code(400);
    echo json_encode(['error' => 'Invalid clear_time']);
    exit;
}

$player_name = isset($input['player_name']) ? $input['player_name'] : 'Player';
$clear_time = floatval($input['clear_time']);

try {
    $pdo = getDBConnection();
    
    // ランキングに登録
    $stmt = $pdo->prepare('INSERT INTO rankings (player_name, clear_time) VALUES (:player_name, :clear_time)');
    $stmt->execute([
        ':player_name' => $player_name,
        ':clear_time' => $clear_time
    ]);
    
    // 登録成功
    echo json_encode([
        'success' => true,
        'message' => 'Ranking registered successfully',
        'id' => $pdo->lastInsertId(),
        'clear_time' => $clear_time
    ]);
    
} catch (PDOException $e) {
    http_response_code(500);
    echo json_encode(['error' => 'Failed to register: ' . $e->getMessage()]);
}
?>