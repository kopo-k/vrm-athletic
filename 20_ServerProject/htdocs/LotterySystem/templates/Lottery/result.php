<h2>くじ引き</h2>
<h3>結果</h3>
<h4><?php echo $result['name']; ?></h4>
<h3><?php echo $result['message']; ?></h3>
</br>
<h3><?php echo $pickup ?></h3>

<span size="5" color="15848F"><b>
    <?=$this->Form->create(null, ['url' => ['controller' => 'Lottery', 'action' => 'index'], 'type' => 'post'])?>
    <?=$this->Form->submit('戻る')?>
    <?=$this->Form->end()?>
</b></span>
  


