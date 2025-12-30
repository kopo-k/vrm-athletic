<span size="5" color="15848F"><b>
<?=$this->Form->create('ranking', ['url' => ['action' => 'result'], 'type' => 'post'])?>
<?=$this->Form->hidden( "id", [ "value" => 1 ] ) ?>
<?=$this->Form->submit('Aさんに投票')?>
<?=$this->Form->end()?>
</b></span>

<span size="5" color="15848F"><b>
<?=$this->Form->create('ranking', ['url' => ['action' => 'result'], 'type' => 'post'])?>
<?=$this->Form->hidden( "id", [ "value" => 2 ] ) ?>
<?=$this->Form->submit('Bさんに投票')?>
<?=$this->Form->end()?>
</b></span>

<span size="5" color="15848F"><b>
<?=$this->Form->create('ranking', ['url' => ['action' => 'result'], 'type' => 'post'])?>
<?=$this->Form->hidden( "id", [ "value" => 3 ] ) ?>
<?=$this->Form->submit('Cさんに投票')?>
<?=$this->Form->end()?>
</b></span>
