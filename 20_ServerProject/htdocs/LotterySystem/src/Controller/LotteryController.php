<?php
namespace App\Controller;

use App\Controller\AppController;

/**
 * Lottery Controller
 *
 *
 * @method \App\Model\Entity\Lottery[]|\Cake\Datasource\ResultSetInterface paginate($object = null, array $settings = [])
 */
class LotteryController extends AppController
{

    /*
    * Index method
    * databaseを使わない設定
    */
    public function index()
    {
    }

    public function result()
    {
		// 「くじ引き」の結果データリストをJSON形式で定義
		// ※ここは後でデータベースから取得するように変更したい。
		$json = '{"list":[
					{
						"name": "あたり",
						"message": "おめでとう！"
					},
					{
						"name": "はずれ",
						"message": "残念、またつぎ！"
					}]
				}';

		// Jsonデータを連想配列にパース(Jsonを解析して連想配列に入れる）
		$array = json_decode( $json , true ) ;
		// パース結果は↓のようになっている。
		//[
		//	'list' => [
		//		(int) 0 => [
		//			'name' => 'あたり',
		//			'message' => 'おめでとう！'
		//		],
		//		(int) 1 => [
		//			'name' => 'はずれ',
		//			'message' => '残念、またつぎ！'
		//		]
		//	]
		//]

		// 「くじ引き」の結果データリストの項目数を取得する。
		$list_max	= count($array["list"]);
		// $list_max には 2　が入る想定。

		// rand（） 関数を利用し、 0～1 の乱数を取得する。
		$pickup		= rand(0, $list_max-1);
		// $pickup には 0～1 が入る想定。

		// 「くじ引き」の結果データリストの 0～1 の配列を返す。
		$result = $array["list"][$pickup];
		//　$result　には
		//　$result['name']		: 'あたり' or 'はずれ' 
		//　$result['message']	: 'おめでとう' or '残念、またつぎ！'
		// が入っている。

		// テンプレート変数に 「くじ引き」の結果データをセットする。
		$this->set('result', $result); 
		$this->set('pickup', $pickup); 

	}
}
