<h2>くじ引き</h2>
<span size="5" color="15848F"><b>
    <?= $this->Form->create(null, ['url' => ['controller' => 'Lottery', 'action' => 'index'], 'type' => 'post']); ?>
    <?=$this->Form->submit('引く?')?>
    <?=$this->Form->end()?>

</b></span>
