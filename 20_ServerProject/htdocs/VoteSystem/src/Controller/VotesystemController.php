<?php
namespace App\Controller;

use App\Controller\AppController;
use Cake\ORM\TableRegistry;

/**
 * Votesystem Controller
 *
 * @property \App\Model\Table\VotesystemTable $Votesystem
 *
 * @method \App\Model\Entity\Votesystem[]|\Cake\Datasource\ResultSetInterface paginate($object = null, array $settings = [])
 */
class VotesystemController extends AppController
{
    public function vote()
    {
        //voteアクション内で特に処理を行わないので記述なし
    }

    public function result()
    {
        //Postデータの取得
        $postData = $this->request->getData('id');

        //レコードの更新
        $voteSystemTable = $this->getTableLocator()->get('votesystem'); //votesystemテーブルの取得
        $tarrgetRecord = $voteSystemTable->get($postData);              //votesystemテーブルから該当IDのレコードを取得
        $tarrgetRecord->Popularity = $tarrgetRecord['Popularity'] + 1;  //レコードのPopularityを更新
        $voteSystemTable->save($tarrgetRecord);                         //更新したレコードの保存
        
        //レコード件数取得
        $recordNum = $this->Votesystem->find()->count();

        //全レコードのPopularityを取得
        for ($i=1; $i <= $recordNum; $i++)
        {
            $tmpRecord = $voteSystemTable->get($i);             //Idが1から順番にレコードを取得
            $this->set('vote'.$i, $tmpRecord['Popularity']);    //該当レコードのPopularityをViewに渡す
        }

        //人気トップを抽出
        $mostPopularity = $this->Votesystem->find()->select(['mostPopularity' => $this->Votesystem->find()->func()->max('Popularity')])->first();   //Popularityの最大値を取得
        $ret = $this->Votesystem->find()->select(['Name'])->where(['Popularity' => (int)$mostPopularity->mostPopularity])->toArray();               //Popularityの最大値と同じレコードを抽出して配列に格納

        //Popularityがトップの名前の表示用の文字列を用意
        $mostPopularityNames = "";
        for ($j=0; $j < count($ret); $j++)
        { 
            $mostPopularityNames = $mostPopularityNames.$ret[$j]['Name']."さん ";
        }

        $this->set('mostPopularity', $mostPopularityNames); //人気トップのNameをViewに渡す

    }
}
