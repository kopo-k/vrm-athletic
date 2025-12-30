<?php
declare(strict_types=1);

namespace App\Model\Table;

use Cake\ORM\Query\SelectQuery;
use Cake\ORM\RulesChecker;
use Cake\ORM\Table;
use Cake\Validation\Validator;

/**
 * Messageboard Model
 *
 * @method \App\Model\Entity\Messageboard newEmptyEntity()
 * @method \App\Model\Entity\Messageboard newEntity(array $data, array $options = [])
 * @method array<\App\Model\Entity\Messageboard> newEntities(array $data, array $options = [])
 * @method \App\Model\Entity\Messageboard get(mixed $primaryKey, array|string $finder = 'all', \Psr\SimpleCache\CacheInterface|string|null $cache = null, \Closure|string|null $cacheKey = null, mixed ...$args)
 * @method \App\Model\Entity\Messageboard findOrCreate($search, ?callable $callback = null, array $options = [])
 * @method \App\Model\Entity\Messageboard patchEntity(\Cake\Datasource\EntityInterface $entity, array $data, array $options = [])
 * @method array<\App\Model\Entity\Messageboard> patchEntities(iterable $entities, array $data, array $options = [])
 * @method \App\Model\Entity\Messageboard|false save(\Cake\Datasource\EntityInterface $entity, array $options = [])
 * @method \App\Model\Entity\Messageboard saveOrFail(\Cake\Datasource\EntityInterface $entity, array $options = [])
 * @method iterable<\App\Model\Entity\Messageboard>|\Cake\Datasource\ResultSetInterface<\App\Model\Entity\Messageboard>|false saveMany(iterable $entities, array $options = [])
 * @method iterable<\App\Model\Entity\Messageboard>|\Cake\Datasource\ResultSetInterface<\App\Model\Entity\Messageboard> saveManyOrFail(iterable $entities, array $options = [])
 * @method iterable<\App\Model\Entity\Messageboard>|\Cake\Datasource\ResultSetInterface<\App\Model\Entity\Messageboard>|false deleteMany(iterable $entities, array $options = [])
 * @method iterable<\App\Model\Entity\Messageboard>|\Cake\Datasource\ResultSetInterface<\App\Model\Entity\Messageboard> deleteManyOrFail(iterable $entities, array $options = [])
 */
class MessageboardTable extends Table
{
    /**
     * Initialize method
     *
     * @param array<string, mixed> $config The configuration for the Table.
     * @return void
     */
    public function initialize(array $config): void
    {
        parent::initialize($config);

        $this->setTable('messageboard');
        $this->setDisplayField('Name');
        $this->setPrimaryKey('id');
    }

    /**
     * Default validation rules.
     *
     * @param \Cake\Validation\Validator $validator Validator instance.
     * @return \Cake\Validation\Validator
     */
    public function validationDefault(Validator $validator): Validator
    {
        $validator
            ->scalar('Name')
            ->maxLength('Name', 30)
            ->requirePresence('Name', 'create')
            ->notEmptyString('Name');

        $validator
            ->scalar('Message')
            ->maxLength('Message', 128)
            ->requirePresence('Message', 'create')
            ->notEmptyString('Message');

        $validator
            ->dateTime('Date')
            ->notEmptyDateTime('Date');

        return $validator;
    }
}
