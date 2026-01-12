<?php
require_once 'config.php';

// 取得件数（デフォルト5件）
$limit = isset($_GET['limit']) ? intval($_GET['limit']) : 5;

try {
    $pdo = getDBConnection();
    
    // ランキングを取得（クリアタイムが早い順）
    $stmt = $pdo->prepare('
        SELECT id, player_name, clear_time, created_at 
        FROM rankings 
        ORDER BY clear_time ASC 
        LIMIT :limit
    ');
    $stmt->bindValue(':limit', $limit, PDO::PARAM_INT);
    $stmt->execute();
    
    $rankings = $stmt->fetchAll(PDO::FETCH_ASSOC);
    
    echo json_encode([
        'success' => true,
        'rankings' => $rankings
    ]);
    
} catch (PDOException $e) {
    http_response_code(500);
    echo json_encode(['error' => 'Failed to get rankings: ' . $e->getMessage()]);
}
?>